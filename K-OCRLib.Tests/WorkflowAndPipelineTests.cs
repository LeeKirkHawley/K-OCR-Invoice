using K_OCR.Services.Workflow;
using K_OCRLib.Identity;
using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class WorkflowAndPipelineTests
{
    [Fact]
    public async Task InvoiceProcessingService_ProcessFileAsync_RunsWorkflowAndReturnsJson()
    {
        Mock<ILogger<InvoiceProcessingService>> mockLogger = new Mock<ILogger<InvoiceProcessingService>>();
        Mock<IOrgDatabaseService> mockOrgDatabaseService = new Mock<IOrgDatabaseService>();
        var invoice = new InvoiceDto { VendorName = "Acme Corp" };
        var workflow = CreateWorkflow(invoice);
        var service = new InvoiceProcessingService(
            Mock.Of<IFileService>(),
            workflow,
            mockOrgDatabaseService.Object,
            new ConfigurationBuilder().Build(),
            mockLogger.Object);

        var result = await service.ProcessFileAsync("invoice.pdf", "artifacts", new Organization(), new Batch());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Context);
        Assert.Contains("Acme Corp", result.Json);
    }

    [Fact]
    public async Task AzureOcrStep_DelegatesToInvoiceService()
    {
        var invoiceSvc = new Mock<IInvoiceService>();
        invoiceSvc.Setup(s => s.RunAzureInvoiceParse("invoice.pdf"))
            .ReturnsAsync([new InvoiceDto { InvoiceId = "INV-1" }]);

        var step = new AzureOcrStep(invoiceSvc.Object, Mock.Of<ILogger<AzureOcrStep>>());
        var context = new PipelineContext { InputPath = "invoice.pdf" };

        await step.ExecuteAsync(context);

        Assert.Single(context.Layout!);
        Assert.Equal("INV-1", context.Layout![0].InvoiceId);
    }

    [Fact]
    public async Task TesseractOcrStep_CatchesServiceExceptions()
    {
        var tess = new Mock<ITesseractValidationService>();
        tess.Setup(s => s.ExtractTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var step = new TesseractOcrStep(tess.Object, Mock.Of<ILogger<TesseractOcrStep>>());
        var context = new PipelineContext { InputPath = "invoice.pdf" };

        await step.ExecuteAsync(context);

        Assert.Null(context.TesseractOcrText);
    }

    [Fact]
    public async Task TesseractValidationStep_SkipsWhenNoText()
    {
        var validation = new Mock<IInvoiceValidationService>();
        var step = new TesseractValidationStep(validation.Object);

        await step.ExecuteAsync(new PipelineContext { Layout = [new InvoiceDto()], TesseractOcrText = null });

        validation.Verify(v => v.ValidateAgainstTesseract(It.IsAny<InvoiceDto>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnrichmentStep_DelegatesToInvoiceEnrichmentService()
    {
        var enrichment = new Mock<IInvoiceEnrichmentService>();
        var step = new EnrichmentStep(enrichment.Object);
        var invoice = new InvoiceDto();

        await step.ExecuteAsync(new PipelineContext
        {
            Layout = [invoice],
            TesseractOcrText = "USD"
        });

        enrichment.Verify(s => s.DetectCountryAndCurrency(invoice, "USD"), Times.Once);
    }

    //[Fact]
    //public async Task SaveContextStep_WritesPipelineContext()
    //{
    //    var fileService = new Mock<IFileService>();
    //    var step = new SaveContextStep(fileService.Object);
    //    var context = new PipelineContext { InputPath = "invoice.pdf" };

    //    await step.ExecuteAsync(context);

    //    fileService.Verify(s => s.SaveContextAsync("invoice.pdf", context), Times.Once);
    //}

    private static InvoiceProcessingWorkflow CreateWorkflow(InvoiceDto invoice)
    {
        var invoiceService = new Mock<IInvoiceService>();
        var orgDatabaseService = new Mock<IOrgDatabaseService>();
        invoiceService.Setup(s => s.RunAzureInvoiceParse("invoice.pdf"))
            .ReturnsAsync([invoice]);

        var tessService = new Mock<ITesseractValidationService>();
        tessService.Setup(s => s.ExtractTextAsync("invoice.pdf", "artifacts", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Acme Corp");

        var validation = new Mock<IInvoiceValidationService>();
        var enrichment = new Mock<IInvoiceEnrichmentService>();
        var fileService = new Mock<IFileService>();

        return new InvoiceProcessingWorkflow(
            new AzureOcrStep(invoiceService.Object, Mock.Of<ILogger<AzureOcrStep>>()),
            new TesseractOcrStep(tessService.Object, Mock.Of<ILogger<TesseractOcrStep>>()),
            new TesseractValidationStep(validation.Object),
            new EnrichmentStep(enrichment.Object),
            new SaveContextStep(fileService.Object, orgDatabaseService.Object),
            Mock.Of<ILogger<InvoiceProcessingWorkflow>>());
    }
}
