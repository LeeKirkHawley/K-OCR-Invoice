using K_OCRLib.Services;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using K_OCRLib.Models;
using K_OCRLib.Data;

// ── Per-org DB test helper ────────────────────────────────────────────────────
// Creates a temporary org folder with a migrated per-org kocr.db for testing.
// Usage:
//   await using var helper = await PerOrgDbHelper.CreateAsync();
//   var db = helper.DbContext;
//
public sealed class PerOrgDbHelper : IAsyncDisposable
{
    public string OrgFolder { get; }
    public KOCRDbContext DbContext { get; }

    private PerOrgDbHelper(string orgFolder, KOCRDbContext db)
    {
        OrgFolder = orgFolder;
        DbContext = db;
    }

    /// <summary>
    /// Creates a temp org folder, initialises the per-org SQLite file with the
    /// current EF Core schema (<see cref="KOCRDbContext.MigrateAsync"/>), and
    /// returns an open <see cref="KOCRDbContext"/> pointed at it.
    /// </summary>
    public static async Task<PerOrgDbHelper> CreateAsync(string orgName = "TestOrg")
    {
        var orgFolder = Path.Combine(Path.GetTempPath(), $"kocr-test-{orgName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(orgFolder);

        var dbPath = Path.Combine(orgFolder, "kocr.db");
        var options = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new KOCRDbContext(options);
        await db.Database.MigrateAsync();

        return new PerOrgDbHelper(orgFolder, db);
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        if (Directory.Exists(OrgFolder))
            Directory.Delete(OrgFolder, recursive: true);
    }
}


public class MockDatabaseService
{
    private readonly Dictionary<string, Invoice> _mockData = new();

    public async Task<Invoice?> GetInvoiceByFilePathAsync(string filePath)
    {
        _mockData.TryGetValue(filePath, out var invoice);
        return await Task.FromResult(invoice);
    }

    public async Task<Invoice> SaveInvoiceAsync(Invoice invoice)
    {
        _mockData[invoice.FilePath ?? string.Empty] = invoice;
        return await Task.FromResult(invoice);
    }

    public async Task<bool> IsDatabaseAvailableAsync()
    {
        return await Task.FromResult(true);
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {

        Program program = new Program();
        //program.ReadJsonTest();
        //await program.ParseInvoiceTest();
        //await program.ParseMultipleInvoicesTest();
        await program.ParsePdfTest();
    }

    private bool ReadJsonTest()
    {
        // This test is disabled as it requires database service
        // var mockDb = new MockDatabaseService();
        // var fileService = new FileService(mockDb);
        // string json = fileService.ReadJsonFromDisk("C:/OCR/Invoices/Sample-Invoice-printable.json");
        // return !string.IsNullOrEmpty(json);
        return true;
    }   

    private async Task<bool> ParseInvoiceTest()
    {
        var invoiceService = new InvoiceService();

        var sw = Stopwatch.StartNew();
        List<InvoiceDto> results = await invoiceService.RunAzureInvoiceParse("C:/OCR/Invoices/Sample-Invoice-printable.png");

        sw.Stop();
        
        Console.WriteLine($"RunAzureInvoiceParse completed in {sw.Elapsed.TotalMilliseconds:F0} ms ({sw.Elapsed}).");

        Console.WriteLine($"Parsed C:/OCR/Invoices/Sample-Invoice-printable.png: {results}");

        return true;
    }

    private async Task<bool> ParseMultipleInvoicesTest()
    {
        var invoiceService = new InvoiceService();

        var sw = Stopwatch.StartNew();
        var t1 = invoiceService.RunAzureInvoiceParse("C:/OCR/Invoices/Sample-Invoice-printable.png");
        var t2 = invoiceService.RunAzureInvoiceParse("C:/OCR/Invoices/invoice-template-us-neat-750px.png");
        var t3 = invoiceService.RunAzureInvoiceParse("C:/OCR/Invoices/batch1-0001.jpg");
        var t4 = invoiceService.RunAzureInvoiceParse("C:/OCR/Invoices/IN52391329.pdf");

        var results = await Task.WhenAll(t1, t2, t3, t4);

        sw.Stop();

        Console.WriteLine($"RunAzureInvoiceParse completed in {sw.Elapsed.TotalMilliseconds:F0} ms ({sw.Elapsed}).");

        Console.WriteLine($"Parsed JSON 1: {results[0]}");
        Console.WriteLine($"Parsed JSON 2: {results[1]}");
        Console.WriteLine($"Parsed JSON 3: {results[2]}");
        Console.WriteLine($"Parsed JSON 4: {results[3]}");

        return true;
    }

    private async Task<bool> ParsePdfTest()
    {
        var invoiceService = new InvoiceService();

        var sw = Stopwatch.StartNew();
        var t1 = invoiceService.RunAzureInvoiceParse("C:/OCR/Invoices/IN52391329.pdf");

        var results = await Task.WhenAll(t1);

        sw.Stop();

        Console.WriteLine($"RunAzureInvoiceParse completed in {sw.Elapsed.TotalMilliseconds:F0} ms ({sw.Elapsed}).");

        Console.WriteLine($"Parsed JSON 4: {results[0]}");

        return true;
    }
}