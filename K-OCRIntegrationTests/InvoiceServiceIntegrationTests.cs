using System.Diagnostics;
using K_OCRLib.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace K_OCRIntegrationTests;

/// <summary>
/// Calls the real Azure Document Intelligence "prebuilt-invoice" model — no mocks.
/// Requires an "Azure:CognitiveServicesKey" (env var Azure__CognitiveServicesKey) and a
/// local sample invoice file. Both are developer-machine-only, so each test skips itself
/// (rather than failing the build/CI) when either prerequisite is missing.
/// </summary>
public class InvoiceServiceIntegrationTests
{
    private const string SampleInvoicePath = "C:/OCR/Invoices/Sample-Invoice-printable.png";
    private const string SamplePdfPath = "C:/OCR/Invoices/IN52391329.pdf";

    [Fact]
    public async Task RunAzureInvoiceParse_ParsesRealSampleImage()
    {
        // 9/30/2026 - works
        if (!TryCreateConfiguredInvoiceService(out var invoiceService, out var skipReason))
        {
            Console.WriteLine($"Skipping: {skipReason}");
            return;
        }

        if (!File.Exists(SampleInvoicePath))
        {
            Console.WriteLine($"Skipping: sample file not found at {SampleInvoicePath}.");
            return;
        }

        var sw = Stopwatch.StartNew();
        var results = await invoiceService!.RunAzureInvoiceParse(SampleInvoicePath);
        sw.Stop();

        Console.WriteLine($"RunAzureInvoiceParse completed in {sw.Elapsed}.");
        Assert.NotNull(results);
    }

    [Fact]
    public async Task RunAzureInvoiceParse_ParsesRealSamplePdf()
    {
        if (!TryCreateConfiguredInvoiceService(out var invoiceService, out var skipReason))
        {
            Console.WriteLine($"Skipping: {skipReason}");
            return;
        }

        if (!File.Exists(SamplePdfPath))
        {
            Console.WriteLine($"Skipping: sample file not found at {SamplePdfPath}.");
            return;
        }

        var results = await invoiceService!.RunAzureInvoiceParse(SamplePdfPath);

        Assert.NotNull(results);
    }

    private static bool TryCreateConfiguredInvoiceService(out InvoiceService? invoiceService, out string skipReason)
    {
        IConfigurationRoot? configuration = null;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddUserSecrets<InvoiceServiceIntegrationTests>()
                .AddEnvironmentVariables()
                .Build();
        }
        catch(Exception ex)
        {
            invoiceService = null;
            skipReason = $"Failed to build configuration: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration["Azure:CognitiveServicesKey"]))
        {
            invoiceService = null;
            skipReason = "Azure:CognitiveServicesKey is not configured (set via 'dotnet user-secrets set \"Azure:CognitiveServicesKey\" \"<key>\"' or env var Azure__CognitiveServicesKey).";
            return false;
        }

        invoiceService = new InvoiceService(configuration, maxConcurrentRequests: 1, logger: NullLogger<InvoiceService>.Instance);
        skipReason = string.Empty;
        return true;
    }
}
