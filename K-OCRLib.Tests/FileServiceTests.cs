using K_OCRLib.Models;
using K_OCRLib.Data;
using K_OCRLib.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class FileServiceTests
{
    [Fact]
    public async Task LoadContextAsync_ReturnsSavedPipelineContext()
    {
        var (dbService, dbPath) = CreateDatabaseService();
        try
        {
            await SeedInvoiceAsync(dbPath, filePath: "invoice.pdf", ocrText: """{"InputPath":"invoice.pdf","TesseractOcrText":"tess"}""");
            var service = new FileService(dbService, Mock.Of<ILogger<FileService>>());

            var context = await service.LoadContextAsync("invoice.pdf");

            Assert.NotNull(context);
            Assert.Equal("invoice.pdf", context!.InputPath);
            Assert.Equal("tess", context.TesseractOcrText);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SaveValidatedLayoutAsync_UpdatesInvoiceFields()
    {
        var (dbService, dbPath) = CreateDatabaseService();
        try
        {
            var invoiceId = await SeedInvoiceAsync(dbPath, filePath: "invoice.pdf");
            var service = new FileService(dbService, Mock.Of<ILogger<FileService>>());

            await service.SaveValidatedLayoutAsync("invoice.pdf", new InvoiceDto
            {
                VendorName = "Acme",
                CustomerName = "Contoso",
                InvoiceId = "INV-1",
                Subtotal = 10m,
                TotalTax = 2m,
                Discount = 3m,
                Total = 15m,
                InvoiceDate = "2026-01-01",
                DueDate = "2026-02-01",
                Notes = "note",
                VendorCountry = "US",
                CurrencyCode = "USD",
                IsValidationAccepted = true
            });

            await using var verify = new KOCRDbContext(new DbContextOptionsBuilder<KOCRDbContext>().UseSqlite($"Data Source={dbPath}").Options);
            var row = await verify.Invoices.FindAsync(invoiceId);

            Assert.NotNull(row);
            Assert.Equal("Acme", row!.VendorName);
            Assert.True(row.IsValidationAccepted);
            Assert.NotNull(row.ValidatedOcrText);
            Assert.NotNull(row.ProcessedAtUtc);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (DatabaseService DbService, string DbPath) CreateDatabaseService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"files-{Guid.NewGuid()}.db");
        var factory = new DirectDbContextFactory(dbPath);
        var dbService = new DatabaseService(factory, Mock.Of<ILogger<DatabaseService>>());
        return (dbService, dbPath);
    }

    private static async Task<int> SeedInvoiceAsync(string dbPath, string filePath, string? ocrText = null)
    {
        var options = new DbContextOptionsBuilder<KOCRDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        await using var db = new KOCRDbContext(options);
        await db.Database.MigrateAsync();

        var batch = new Batch
        {
            Name = "Batch 1",
            BatchNumber = 1,
            FolderPath = "/tmp",
            CreatedByUserId = "user",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Batches.Add(batch);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            BatchId = batch.BatchId,
            FilePath = filePath,
            UploadedAtUtc = DateTime.UtcNow,
            OcrText = ocrText,
            TotalPages = 1
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private static void Cleanup(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }
}
