using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class AzureAndInvoiceServiceTests
{
    [Fact]
    public async Task AzureService_RunAzureOcrAsync_ThrowsWhenInputFileMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureCognitiveServicesEndpoint"] = "https://example.test/",
                ["AzureCognitiveServicesKey"] = "key"
            })
            .Build();

        var service = new AzureService(config, Mock.Of<IFileService>());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.RunAzureOcrAsync([Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg")]));
    }

    [Fact]
    public async Task InvoiceService_RunAzureInvoiceParse_ThrowsWhenInputFileMissing()
    {
        var service = new InvoiceService(logger: Mock.Of<ILogger<InvoiceService>>());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.RunAzureInvoiceParse(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf")));
    }
}
