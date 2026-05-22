using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Models;
using K_OCR.Services;
using KOCRAsp.Controllers;
using KOCRAsp.Models;
using KOCRAsp.Services;
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
    private readonly Mock<IDocumentExportService> _mockExportSvc;
    private readonly Mock<IOrgConfigService> _mockOrgConfigSvc;
    private readonly Mock<ITenantContext> _mockTenantContext;
    private readonly Mock<IInvoiceActionService> _mockInvoiceActionSvc;
    private readonly Mock<IConfigurationService> _mockConfigSvc;
    private readonly Mock<IBatchActionService> _mockBatchActionSvc;
    private readonly Mock<IBatchNotificationService> _mockBatchNotificationSvc;
    private readonly Mock<ILogger<HomeController>> _mockLogger;
    private readonly DatabaseService _dbSvc;
    private readonly IBatchChangeNotifier _batchNotifier;
    private readonly HomeController _controller;

    private const string UserId = "user-id-123";
    private const string OrgId  = "org-id-123";
    private const string OrgName = "TestOrg";

    public HomeControllerTests()
    {
        _mockOcrSvc            = new Mock<IInvoiceProcessingService>();
        _mockBatchSvc          = new Mock<IBatchService>();
        _mockFileSvc           = new Mock<IFileService>();
        _mockExportSvc         = new Mock<IDocumentExportService>();
        _mockOrgConfigSvc      = new Mock<IOrgConfigService>();
        _mockTenantContext     = new Mock<ITenantContext>();
        _mockInvoiceActionSvc  = new Mock<IInvoiceActionService>();
        _mockConfigSvc         = new Mock<IConfigurationService>();
        _mockBatchActionSvc    = new Mock<IBatchActionService>();
        _mockBatchNotificationSvc = new Mock<IBatchNotificationService>();
        _mockLogger            = new Mock<ILogger<HomeController>>();
        _batchNotifier         = new BatchChangeNotifier();

        _mockTenantContext.Setup(t => t.OrganizationName).Returns(OrgName);
        _mockOrgConfigSvc.Setup(s => s.LoadAsync(OrgName))
                         .ReturnsAsync(new OrgConfig { MinConfidenceThreshold = 0.8 });
        _mockConfigSvc.Setup(s => s.LoadSettingsAsync(null))
                      .ReturnsAsync(new AppSettings());

        // DatabaseService is a concrete class; construct it with a mock factory.
        var mockContextFactory = new Mock<IDbContextFactory<KOCRDbContext>>();
        _dbSvc = new DatabaseService(mockContextFactory.Object, Mock.Of<ILogger<DatabaseService>>());

        _controller = new HomeController(
            _mockOcrSvc.Object,
            _mockBatchSvc.Object,
            _mockFileSvc.Object,
            _mockExportSvc.Object,
            _dbSvc,
            _mockOrgConfigSvc.Object,
            _mockTenantContext.Object,
            _mockInvoiceActionSvc.Object,
            _mockConfigSvc.Object,
            _mockBatchActionSvc.Object,
            _batchNotifier,
            _mockBatchNotificationSvc.Object,
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
    public async Task GetFiles_ReturnsPaginatedShape()
    {
        // No current batch in session → returns empty paginated result
        SetControllerContext(currentBatchId: null);

        var result = await _controller.GetFiles();

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        var type = value.GetType();
        Assert.NotNull(type.GetProperty("items"));
        Assert.NotNull(type.GetProperty("total"));
        Assert.NotNull(type.GetProperty("page"));
        Assert.NotNull(type.GetProperty("pageSize"));
        Assert.NotNull(type.GetProperty("totalPages"));
        Assert.Equal(0, (int)type.GetProperty("total")!.GetValue(value)!);
    }

    [Fact]
    public async Task StartOcr_ValidPath_ReturnsInvoiceResult()
    {
        SetControllerContext();

        var processingResult = new ProcessingResult
        {
            Context = new K_OCR.Models.PipelineContext(),
            Json = "{}"
        };
        _mockOcrSvc
            .Setup(s => s.ProcessFileAsync(@"C:\invoices\file.pdf", @"C:\Artifacts", 0.8))
            .ReturnsAsync(processingResult);

        var invoice = new InvoiceDto { VendorName = "Acme Corp" };
        _mockOcrSvc
            .Setup(s => s.LoadInvoiceAsync(@"C:\invoices\file.pdf"))
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
            .Setup(s => s.LoadInvoiceAsync(@"C:\invoices\file.pdf"))
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
