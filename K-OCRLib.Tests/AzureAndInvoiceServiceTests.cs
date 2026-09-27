using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class AzureAndInvoiceServiceTests
{
    //[Fact]
    //public async Task InvoiceService_RunAzureInvoiceParse_ThrowsWhenInputFileMissing()
    //{
    //    var service = new InvoiceService(logger: Mock.Of<ILogger<InvoiceService>>());

    //    await Assert.ThrowsAsync<FileNotFoundException>(() =>
    //        service.RunAzureInvoiceParse(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf")));
    //}

    //[Fact]
    //public void InvoiceService_GetFieldLookupMap_ReturnsMapping()
    //{
    //    var service = new InvoiceService(logger: Mock.Of<ILogger<InvoiceService>>());

    //    // GetFieldLookupMap is internal/private, but we can test via the field registry
    //    // that the service is properly initialized
    //    Assert.NotNull(service);
    //}
}

