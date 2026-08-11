using K_OCRLib.Data;
using K_OCRLib.Identity;
using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using KOCRAsp.Controllers;
using KOCRAsp.Models;
using KOCRAsp.Services;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OCRQueue.Abstractions;
using OCRQueue.Models;

namespace KOCRAsp.Tests.Controllers;

public class HomeControllerTests
{
    private readonly Mock<IHomePageService> _mockHomePageSvc;
    private readonly Mock<IHomeOcrService> _mockHomeOcrSvc;
    private readonly Mock<IHomeExportService> _mockHomeExportSvc;
    private readonly Mock<IOrganizationActivityLogService> _mockOrgLogSvc;
    private readonly Mock<ITrialOrganizationLimitService> _mockTrialLimitSvc;
    private readonly Mock<ITenantContext> _mockTenantContext;
    private readonly Mock<IConfigurationService> _mockConfigSvc;
    private readonly Mock<IOcrQueueRepository> _mockOcrQueueRepo;
    private readonly Mock<IOrgConfigService> _mockOrgConfigSvc;
    private readonly Mock<K_OCRLib.Services.IBatchChangeNotifier> _mockBatchNotifier;
    private readonly Mock<IOrgDatabaseService> _mockOrgDatabaseService;
    private readonly HomeController _controller;
    private readonly Mock<ILogger<HomeController>> _logger;

    private const string UserId = "user-id-123";
    private const string OrgId = "org-id-123";
    private const string OrgName = "TestOrg";

    public HomeControllerTests()
    {
        _mockHomePageSvc = new Mock<IHomePageService>();
        _mockHomeOcrSvc = new Mock<IHomeOcrService>();
        _mockHomeExportSvc = new Mock<IHomeExportService>();
        _mockOrgLogSvc = new Mock<IOrganizationActivityLogService>();
        _mockTrialLimitSvc = new Mock<ITrialOrganizationLimitService>();
        _mockTenantContext = new Mock<ITenantContext>();
        _mockConfigSvc = new Mock<IConfigurationService>();
        _mockOcrQueueRepo = new Mock<IOcrQueueRepository>();
        _mockOrgDatabaseService = new Mock<IOrgDatabaseService>();
        _mockOrgConfigSvc = new Mock<IOrgConfigService>();
        _mockBatchNotifier = new Mock<K_OCRLib.Services.IBatchChangeNotifier>();

        _logger = new Mock<ILogger<HomeController>>(); 

        _mockTenantContext.Setup(t => t.OrganizationId).Returns(OrgId);
        _mockTenantContext.Setup(t => t.OrganizationName).Returns(OrgName);
        _mockTenantContext.Setup(t => t.IsGuestOrganization).Returns(false);
        _mockTenantContext.Setup(t => t.IsBetaTestOrganization).Returns(false);
        _mockTenantContext.Setup(t => t.IsTrialOrganization).Returns(false);
        _mockTenantContext.Setup(t => t.StripeSubscriptionStatus).Returns("active");
        _mockConfigSvc.Setup(s => s.GetGuestMaxBatches()).Returns(3);
        _mockConfigSvc.Setup(s => s.GetMaxInvoicesPerBatch(true)).Returns(25);
        _mockConfigSvc.Setup(s => s.GetMaxPagesPerInvoice(true)).Returns(20);
        _mockOcrQueueRepo
            .Setup(r => r.GetPendingJobsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OcrJobRecord>());
        _mockTrialLimitSvc
            .Setup(s => s.GetCurrentStatusAsync())
            .ReturnsAsync(new TrialOrganizationLimitStatus(false, false, 500, 0, 0, 0));
        _mockOrgConfigSvc
            .Setup(s => s.LoadAsync(OrgName))
            .ReturnsAsync(new K_OCRLib.Configuration.OrgConfig { MinConfidenceThreshold = 0.8 });

        _controller = new HomeController(
            _mockHomePageSvc.Object,
            _mockHomeOcrSvc.Object,
            _mockHomeExportSvc.Object,
            _mockOrgLogSvc.Object,
            _mockTrialLimitSvc.Object,
            _mockTenantContext.Object,
            _mockConfigSvc.Object,
            _mockOrgConfigSvc.Object,
            _mockOcrQueueRepo.Object,
            _mockOrgDatabaseService.Object,
            _logger.Object,
            _mockBatchNotifier.Object);
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
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(),
                Request =
                {
                    Path = "/"
                }
            }
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
    public async Task Index_Anonymous_DuplicateRouteRedirectsToRoot()
    {
        SetAnonymousControllerContext();
        _controller.ControllerContext.HttpContext.Request.Path = "/Home/Index";

        var result = await _controller.Index();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
    }

    [Fact]
    public async Task Index_ReturnsViewWithModel()
    {
        (ApplicationDbContext? db, ServiceProvider? provider, SqliteConnection? connection) = await CreateIdentityHarnessAsync();

        Organization org = new Organization { Id = OrgId, Name = "Acme" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        UserManager<ApplicationUser> userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await provider.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new IdentityRole("OrganizationAdmin"));

        BatchSummary[] batches = new[] { new BatchSummary { BatchId = 1, Name = "Batch A", OrganizationId = OrgId } };
        _mockHomePageSvc
            .Setup(s => s.GetBatchesForOrgAsync(OrgId))
            .ReturnsAsync(batches);

        SetControllerContext();
        
        // Create a request scope and set it on the HttpContext so ValidateOrgAsync can access ApplicationDbContext
        var scope = provider.CreateAsyncScope();
        _controller.ControllerContext.HttpContext.RequestServices = scope.ServiceProvider;

        try
        {
            IActionResult result = await _controller.Index();

            ViewResult viewResult = Assert.IsType<ViewResult>(result);
            HomeIndexViewModel model = Assert.IsType<HomeIndexViewModel>(viewResult.Model);
            Assert.Single(model.AvailableBatches);
            Assert.Null(model.CurrentBatch);
        }
        finally
        {
            await scope.DisposeAsync();
            await connection.CloseAsync();
            connection.Dispose();
            provider.Dispose();
        }
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
    public async Task StartOcr_WhenBetaLimitExceeded_ReturnsLimitError()
    {
        SetControllerContext();
        _mockTrialLimitSvc
            .Setup(s => s.GetCurrentStatusAsync())
            .ReturnsAsync(new TrialOrganizationLimitStatus(true, false, 500, 500, 0, 0));
        _mockTenantContext.Setup(t => t.IsBetaTestOrganization).Returns(true);

        var result = await _controller.StartOcr(@"C:\invoices\file.pdf");

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        Assert.False((bool)value.GetType().GetProperty("success")!.GetValue(value)!);
        Assert.True((bool)value.GetType().GetProperty("betaLimitExceeded")!.GetValue(value)!);
        _mockHomeOcrSvc.Verify(
            s => s.StartOcrAsync(It.IsAny<string>(), It.IsAny<HomeTenantInfo>(), It.IsAny<string>(), It.IsAny<BatchSummary?>()),
            Times.Never);
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

    private static async Task<(ApplicationDbContext db, ServiceProvider provider, SqliteConnection connection)> CreateIdentityHarnessAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // Add MVC services needed for View rendering
        services.AddMvc();
        services.AddSession();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        return (db, provider, connection);
    }

}
