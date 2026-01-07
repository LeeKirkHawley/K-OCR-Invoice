using K_OCR.Models;
using K_OCR.Services;
using System.Diagnostics;

public class Program
{
    public static async Task Main(string[] args)
    {

        Program program = new Program();
        //program.ReadJsonTest();
        //await program.ParseInvoiceTest();
        await program.ParseMultipleInvoicesTest();
    }

    private bool ReadJsonTest()
    {
        var fileService = new FileService();
        string json = fileService.ReadJsonFromDisk("C:/OCR/Invoices/Sample-Invoice-printable.json");
        return !string.IsNullOrEmpty(json);
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

        var results = await Task.WhenAll(t1, t2, t3);

        sw.Stop();

        Console.WriteLine($"RunAzureInvoiceParse completed in {sw.Elapsed.TotalMilliseconds:F0} ms ({sw.Elapsed}).");

        Console.WriteLine($"Parsed JSON 1: {results[0]}");
        Console.WriteLine($"Parsed JSON 2: {results[1]}");
        Console.WriteLine($"Parsed JSON 3: {results[2]}");

        return true;
    }
}