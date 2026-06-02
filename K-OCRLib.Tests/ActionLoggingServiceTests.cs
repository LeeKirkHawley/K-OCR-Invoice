using K_OCRLib.Models;
using K_OCRLib.Data;
using K_OCRLib.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class ActionLoggingServiceTests
{
    [Fact]
    public async Task BatchActionService_PersistsAction()
    {
        var (factory, dbPath) = CreateFactory();
        try
        {
            await using var context = factory.CreateDbContext();
            context.Database.Migrate();

            var service = new BatchActionService(factory, Mock.Of<ILogger<BatchActionService>>());
            await service.LogAsync("Created", "Batch 1", "Org A", "user@example.com");

            await using var verify = factory.CreateDbContext();
            var row = await verify.BatchActions.SingleAsync();

            Assert.Equal("Created", row.Action);
            Assert.Equal("Batch 1", row.BatchName);
            Assert.Equal("Org A", row.Organization);
            Assert.Equal("user@example.com", row.OrgUser);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task InvoiceActionService_PersistsAction()
    {
        var (factory, dbPath) = CreateFactory();
        try
        {
            await using var context = factory.CreateDbContext();
            context.Database.Migrate();

            var service = new InvoiceActionService(factory, Mock.Of<ILogger<InvoiceActionService>>());
            await service.LogAsync("OCR", "invoice.pdf", "Batch 1", "user@example.com", 5);

            await using var verify = factory.CreateDbContext();
            var row = await verify.InvoiceActions.SingleAsync();

            Assert.Equal("OCR", row.Action);
            Assert.Equal("invoice.pdf", row.InvoiceName);
            Assert.Equal("Batch 1", row.BatchName);
            Assert.Equal("user@example.com", row.OrgUser);
            Assert.Equal(5, row.PageCount);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (DirectDbContextFactory Factory, string DbPath) CreateFactory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"actions-{Guid.NewGuid()}.db");
        return (new DirectDbContextFactory(dbPath), dbPath);
    }

    private static void Cleanup(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }
}
