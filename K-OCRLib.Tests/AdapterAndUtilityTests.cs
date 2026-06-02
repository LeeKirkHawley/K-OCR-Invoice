using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SkiaSharp;

namespace K_OCRLib.Tests;

public class AdapterAndUtilityTests
{
    [Fact]
    public async Task DocumentExportService_MethodsCompleteImmediately()
    {
        var service = new DocumentExportService();

        await service.ExportToDocxAsync("text", "out.docx");
        await service.ExportToPdfAsync("out.pdf");
        await service.ExportToCsvAsync([], "out.csv");
    }

    [Fact]
    public void AnalysisService_DetectTables_ReturnsEmptyForNoInput()
    {
        var service = new AnalysisService();

        var rows = service.DetectTables([], null!);

        Assert.Empty(rows);
    }

    [Fact]
    public void InvoiceEnrichmentService_DetectsCurrencyAndCountry()
    {
        var service = new InvoiceEnrichmentService();
        var invoice = new InvoiceDto { VendorName = "Acme Corp" };

        service.DetectCountryAndCurrency(invoice, "Acme Corp 123 Main St, Seattle, WA 98101 USD");

        Assert.Equal("USD", invoice.CurrencyCode);
        Assert.Equal("United States", invoice.VendorCountry);
    }

    [Fact]
    public async Task TesseractValidationService_ReturnsEmptyForUnsupportedFile()
    {
        var service = new TesseractValidationService(Mock.Of<IImageService>(), tessdataPath: Path.Combine(Path.GetTempPath(), "tessdata"));

        var text = await service.ExtractTextAsync(Path.Combine(Path.GetTempPath(), "invoice.txt"));

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task TesseractValidationService_ReturnsEmptyWhenPdfHasNoPages()
    {
        var imageService = new Mock<IImageService>();
        imageService.Setup(s => s.ConvertPdfToAllPngsAsync("invoice.pdf", null, It.IsAny<int>()))
            .ReturnsAsync([]);

        var service = new TesseractValidationService(imageService.Object, tessdataPath: Path.Combine(Path.GetTempPath(), "tessdata"));
        var text = await service.ExtractTextAsync("invoice.pdf");

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task ImageService_CombineImagesVerticallyAsync_WritesOutput()
    {
        var service = new ImageService(Mock.Of<ILogger<ImageService>>());
        var tempDir = Path.Combine(Path.GetTempPath(), $"images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var image1 = Path.Combine(tempDir, "1.png");
        var image2 = Path.Combine(tempDir, "2.png");
        var output = Path.Combine(tempDir, "combined.png");
        CreatePng(image1, 20, 10);
        CreatePng(image2, 30, 15);

        try
        {
            var (filePath, heights) = await service.CombineImagesVerticallyAsync([image1, image2], output);

            Assert.Equal(output, filePath);
            Assert.Equal([10, 15], heights);
            Assert.True(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ImageService_ConvertPdfToPngAsync_RejectsNonPdfFiles()
    {
        var service = new ImageService(Mock.Of<ILogger<ImageService>>());
        var tempFile = Path.Combine(Path.GetTempPath(), $"file-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "hello");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ConvertPdfToPngAsync(tempFile, Path.GetTempPath()));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task StripeUsageService_SkipsWhenNotConfigured()
    {
        var service = new StripeUsageService(new ConfigurationBuilder().Build(), Mock.Of<ILogger<StripeUsageService>>());

        await service.ReportUsageAsync("cus_123", 5);

        Assert.False(service.IsStatusActive("canceled"));
        Assert.True(service.IsStatusActive("active"));
    }

    [Fact]
    public async Task StripeProvisioningService_ThrowsWhenSecretMissing()
    {
        var service = new StripeProvisioningService(new ConfigurationBuilder().Build(), Mock.Of<ILogger<StripeProvisioningService>>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCustomerAsync("org-1", "Acme"));
    }

    private static void CreatePng(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }
}
