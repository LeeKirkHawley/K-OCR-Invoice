using K_OCR.Models;
using K_OCR.Data;
using K_OCR.Services;
using KOCRAsp.Controllers;
using KOCRAsp.Models;
using KOCRAsp.Services;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class HomeControllerTests
{
    private readonly Mock<IHomePageService> _mockHomePageSvc;
    private readonly Mock<IHomeOcrService> _mockHomeOcrSvc;
    private readonly Mock<IHomeExportService> _mockHomeExportSvc;
    private readonly Mock<IOrganizationActivityLogService> _mockOrgLogSvc;
    private readonly Mock<ITenantContext> _mockTenantContext;
    private readonly HomeController _controller;

    private const string UserId = "user-id-123";
    private const string OrgId = "org-id-123";
    private const string OrgName = "TestOrg";

    public HomeControllerTests()
    {
        _mockHomePageSvc = new Mock<IHomePageService>();
        _mockHomeOcrSvc = new Mock<IHomeOcrService>();
        _mockHomeExportSvc = new Mock<IHomeExportService>();
        _mockOrgLogSvc = new Mock<IOrganizationActivityLogService>();
        _mockTenantContext = new Mock<ITenantContext>();

        _mockTenantContext.Setup(t => t.OrganizationId).Returns(OrgId);
        _mockTenantContext.Setup(t => t.OrganizationName).Returns(OrgName);
        _mockTenantContext.Setup(t => t.IsGuestOrganization).Returns(false);
        _mockTenantContext.Setup(t => t.StripeSubscriptionStatus).Returns("active");

        _controller = new HomeController(
            _mockHomePageSvc.Object,
            _mockHomeOcrSvc.Object,
            _mockHomeExportSvc.Object,
            _mockOrgLogSvc.Object,
            _mockTenantContext.Object,
            Mock.Of<ILogger<HomeController>>());
    }

    private void SetControllerContext(string? currentBatchId = null)
    {
        var session = ControllerTestHelper.CreateMockSession(currentBatchId);
        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext(
            userId: UserId, orgId: OrgId, session: session.Object);
    }

    private void SetAnonymousControllerContext()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal() }
        };
    }

    [Fact]
    public async Task Index_Anonymous_ReturnsLandingView()
    {
        SetAnonymousControllerContext();

        var result = await _controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Landing", viewResult.ViewName);
    }

    [Fact]
    public async Task Index_ReturnsViewWithModel()
    {
        var batches = new[]
        {
            new BatchSummary { BatchId = 1, Name = "Batch A", OrganizationId = OrgId }
        };
        _mockHomePageSvc
            .Setup(s => s.GetBatchesForOrgAsync(OrgId))
            .ReturnsAsync(batches);

        SetControllerContext();

        var result = await _controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HomeIndexViewModel>(viewResult.Model);
        Assert.Single(model.AvailableBatches);
        Assert.Null(model.CurrentBatch);
    }

    [Fact]
    public async Task GetFiles_ReturnsPaginatedShape()
    {
        SetControllerContext();

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

        var invoice = new InvoiceDto { VendorName = "Acme Corp", PageCount = 5 };
        _mockHomeOcrSvc
            .Setup(s => s.StartOcrAsync(
                @"C:\invoices\file.pdf",
                It.IsAny<HomeTenantInfo>(),
                UserId,
                It.IsAny<BatchSummary?>()))
            .ReturnsAsync(new HomeSingleOcrResult(true, Invoice: invoice));

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
        _mockHomePageSvc
            .Setup(s => s.LoadInvoiceAsync(@"C:\Org\Batch1\Invoices\file.pdf"))
            .ReturnsAsync(invoice);
        _mockHomeExportSvc
            .Setup(s => s.BuildDocxAsync(@"C:\Org\Batch1\Invoices\file.pdf", invoice))
            .ReturnsAsync(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        _mockOrgLogSvc
            .Setup(s => s.LogValidatedInvoiceDownloadAsync(OrgName, "Batch1", It.IsAny<string>(), "file.pdf"))
            .Returns(Task.CompletedTask);

        var result = await _controller.ExportDocx(@"C:\Org\Batch1\Invoices\file.pdf");

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            fileResult.ContentType);
        Assert.Equal("file.docx", fileResult.FileDownloadName);
    }
}
