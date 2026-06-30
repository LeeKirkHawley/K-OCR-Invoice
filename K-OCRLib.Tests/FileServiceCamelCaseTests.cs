using K_OCRLib.Models;
using K_OCRLib.Data;
using K_OCRLib.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class FileServiceCamelCaseTests
{
    [Fact]
    public async Task LoadContextAsync_DeserializesCamelCaseJsonWithTaxRate()
    {
        var (dbService, dbPath) = CreateDatabaseService();
        try
        {
            // Create OcrText JSON with camelCase "taxRate" field
            string ocrText = """
            {
              "inputPath": "invoice.pdf",
              "tesseractOcrText": "tess",
              "layout": [
                {
                  "vendorName": "Acme",
                  "customerName": "Contoso",
                  "invoiceId": "INV-123",
                  "invoiceDate": "2026-01-01",
                  "dueDate": "2026-02-01",
                  "purchaseOrder": "PO-456",
                  "subtotal": 100.0,
                  "totalTax": 10.0,
                  "shipping": 5.0,
                  "total": 115.0,
                  "items": [
                    {
                      "description": "Widget",
                      "quantity": 5.0,
                      "unitPrice": 10.0,
                      "amount": 50.0,
                      "taxRate": "10%",
                      "boundingBoxes": [],
                      "fieldConfidences": {},
                      "confidenceConfirmed": {},
                      "tesseractConfirmed": {}
                    },
                    {
                      "description": "Gadget",
                      "quantity": 2.0,
                      "unitPrice": 25.0,
                      "amount": 50.0,
                      "taxRate": "20%",
                      "boundingBoxes": [],
                      "fieldConfidences": {},
                      "confidenceConfirmed": {},
                      "tesseractConfirmed": {}
                    }
                  ],
                  "fieldBoundingBoxes": {},
                  "fieldConfidences": {},
                  "originalPageWidth": 8.5,
                  "originalPageHeight": 11.0,
                  "pageCount": 1,
                  "tesseractConfirmed": {},
                  "confidenceConfirmed": {},
                  "mathConfirmed": {},
                  //"isValidationAccepted": false,
                  "notes": null,
                  "vendorCountry": null,
                  "currencyCode": "USD"
                }
              ],
              "artifactsDirectory": null,
              "minConfidenceThreshold": null,
              "organization": null,
              "batch": null
            }
            """;

            await SeedInvoiceAsync(dbPath, filePath: "invoice.pdf", ocrText: ocrText);
            var service = new FileService(dbService, Mock.Of<ILogger<FileService>>());

            var context = await service.LoadContextAsync("invoice.pdf");

            Assert.NotNull(context);
            Assert.Equal("invoice.pdf", context!.InputPath);
            Assert.Equal("tess", context.TesseractOcrText);
            
            Assert.NotNull(context.Layout);
            Assert.Single(context.Layout);
            
            var invoice = context.Layout[0];
            Assert.Equal("Acme", invoice.VendorName);
            Assert.Equal("Contoso", invoice.CustomerName);
            Assert.Equal("INV-123", invoice.InvoiceId);
            
            // Verify Items deserialized correctly
            Assert.Equal(2, invoice.Items.Count);
            
            // Verify first item TaxRate deserialized from camelCase
            Assert.NotNull(invoice.Items[0].TaxRate);
            Assert.Equal("10%", invoice.Items[0].TaxRate);
            Assert.Equal("Widget", invoice.Items[0].Description);
            
            // Verify second item TaxRate deserialized from camelCase
            Assert.NotNull(invoice.Items[1].TaxRate);
            Assert.Equal("20%", invoice.Items[1].TaxRate);
            Assert.Equal("Gadget", invoice.Items[1].Description);
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
