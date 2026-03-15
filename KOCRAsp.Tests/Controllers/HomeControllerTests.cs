using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Models;
using K_OCR.Services;
using KOCRAsp.Controllers;
using KOCRAsp.Models;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class HomeControllerTests
{
    private readonly Mock<IInvoiceProcessingService> _mockOcrSvc;
    private readonly Mock<IBatchService> _mockBatchSvc;
    private readonly Mock<IFileService> _mockFileSvc;
    private readonly Mock<IConfigurationService> _mockConfigSvc;
    private readonly Mock<IDocumentExportService> _mockExportSvc;
    private readonly Mock<ILogger<HomeController>> _mockLogger;
    private readonly DatabaseService _dbSvc;
    private readonly HomeController _controller;

    private const string UserId = "user-id-123";
    private const string OrgId = "org-id-123";

    public HomeControllerTests()
    {
        _mockOcrSvc = new Mock<IInvoiceProcessingService>();
        _mockBatchSvc = new Mock<IBatchService>();
        _mockFileSvc = new Mock<IFileService>();
        _mockConfigSvc = new Mock<IConfigurationService>();
        _mockExportSvc = new Mock<IDocumentExportService>();
        _mockLogger = new Mock<ILogger<HomeController>>();

        // DatabaseService is a concrete class; construct it with a mock factory.
        // Tests that don't trigger a session-resolved current batch won't invoke it.
        var mockContextFactory = new Mock<IDbContextFactory<KOCRDbContext>>();
        _dbSvc = new DatabaseService(mockContextFactory.Object, Mock.Of<ILogger<DatabaseService>>());

        _controller = new HomeController(
            _mockOcrSvc.Object,
            _mockBatchSvc.Object,
            _mockFileSvc.Object,
            _mockConfigSvc.Object,
            _mockExportSvc.Object,
            _dbSvc,
            _mockLogger.Object);
    }

    private void SetControllerContext(string? currentBatchId = null)
    {
        var session = ControllerTestHelper.CreateMockSession(currentBatchId);
        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext(
            userId: UserId, orgId: OrgId, session: session.Object);
    }

    [Fact]
    public async Task Index_ReturnsViewWithModel()
    {
        var batches = new[]
        {
            new BatchSummary { BatchId = 1, Name = "Batch A", OrganizationId = OrgId }
        };
        _mockBatchSvc
            .Setup(s => s.GetBatchesForOrgAsync(OrgId))
            .ReturnsAsync(batches);

        // No current batch in session → BuildFileListAsync not called
        SetControllerContext(currentBatchId: null);

        var result = await _controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HomeIndexViewModel>(viewResult.Model);
        Assert.Single(model.AvailableBatches);
        Assert.Null(model.CurrentBatch);
    }

    [Fact]
    public async Task GetFiles_ReturnsBatchFiles()
    {
        // No current batch in session → returns empty list immediately
        SetControllerContext(currentBatchId: null);

        var result = await _controller.GetFiles();

        var json = Assert.IsType<JsonResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<FileListEntry>>(json.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task StartOcr_ValidPath_ReturnsInvoiceResult()
    {
        SetControllerContext();

        var processingResult = new ProcessingResult
        {
            Context = new K_OCR.PipelineService.PipelineContext(),
            Json = "{}"
        };
        _mockOcrSvc
            .Setup(s => s.ProcessFileAsync(@"C:\invoices\file.pdf", false, @"C:\Artifacts"))
            .ReturnsAsync(processingResult);

        var invoice = new InvoiceDto { VendorName = "Acme Corp" };
        _mockOcrSvc
            .Setup(s => s.LoadCachedInvoiceAsync(@"C:\invoices\file.pdf"))
            .ReturnsAsync(invoice);

        var result = await _controller.StartOcr(@"C:\invoices\file.pdf");

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        var successProp = value.GetType().GetProperty("success");
        Assert.NotNull(successProp);
        Assert.True((bool)successProp.GetValue(value)!);
    }

    [Fact]
    public async Task ExportDocx_ValidPath_ReturnsFileResult()
    {
        SetControllerContext();

        var invoice = new InvoiceDto { VendorName = "Acme Corp" };
        _mockOcrSvc
            .Setup(s => s.LoadCachedInvoiceAsync(@"C:\invoices\file.pdf"))
            .ReturnsAsync(invoice);

        // Callback writes a minimal placeholder DOCX so File.ReadAllBytesAsync succeeds
        _mockExportSvc
            .Setup(s => s.ExportToDocxAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, outputPath) =>
                System.IO.File.WriteAllBytes(outputPath, new byte[] { 0x50, 0x4B, 0x03, 0x04 }))
            .Returns(Task.CompletedTask);

        var result = await _controller.ExportDocx(@"C:\invoices\file.pdf");

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            fileResult.ContentType);
        Assert.Equal("file.docx", fileResult.FileDownloadName);
    }
}
