using K_OCRLib.Models;
using K_OCRLib.Models.Api.SuperAdmin;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using KOCRAsp.Controllers;
using KOCRAsp.Models;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OCRQueue.Abstractions;

namespace KOCRAsp.Tests.Controllers;

public class AdminControllerTests
{
    private readonly Mock<ISuperAdminService> _mockSuperAdminSvc;
    private readonly Mock<ISuperAdminDataService> _mockDataSvc;
    private readonly Mock<ILogger<AdminController>> _mockLogger;
    private readonly Mock<IOcrJobQueue> _mockOcrJobQueue;
    private readonly Mock<IOcrQueueProcessor> _mockOcrQueueProcessor;
    private readonly IConfiguration _configuration;
    private readonly AdminController _controller;

    public AdminControllerTests()
    {
        _mockSuperAdminSvc = new Mock<ISuperAdminService>();
        _mockDataSvc = new Mock<ISuperAdminDataService>();
        _mockLogger = new Mock<ILogger<AdminController>>();
        _mockOcrJobQueue = new Mock<IOcrJobQueue>();
        _mockOcrQueueProcessor = new Mock<IOcrQueueProcessor>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeletedOrgRetentionDays"] = "14"
            })
            .Build();

        _controller = new AdminController(
            _mockSuperAdminSvc.Object,
            _mockDataSvc.Object,
            _configuration,
            _mockOcrJobQueue.Object,
            _mockOcrQueueProcessor.Object,
            _mockLogger.Object);

        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext(role: "SuperAdmin");
    }

    [Fact]
    public async Task Index_ReturnsSuperAdminView()
    {
        _mockSuperAdminSvc
            .Setup(s => s.ListOrganizationsAsync())
            .ReturnsAsync(Array.Empty<OrganizationOverview>());

        var result = await _controller.Index();

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task OrganizationDetails_ReturnsViewForExistingOrganization()
    {
        _mockSuperAdminSvc
            .Setup(s => s.ListOrganizationsAsync())
            .ReturnsAsync(new[]
            {
                new OrganizationOverview
                {
                    OrganizationId = "org-1",
                    Name = "Org One",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                }
            });

        var result = await _controller.OrganizationDetails("org-1");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminOrganizationDetailsViewModel>(view.Model);
        Assert.Equal("org-1", model.Organization.OrganizationId);
    }

    [Fact]
    public async Task OrganizationDetails_ReturnsNotFoundForUnknownOrganization()
    {
        _mockSuperAdminSvc
            .Setup(s => s.ListOrganizationsAsync())
            .ReturnsAsync(Array.Empty<OrganizationOverview>());

        var result = await _controller.OrganizationDetails("missing");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AllBatches_ReturnsPagedViewModel()
    {
        var batches = Enumerable.Range(1, 11)
            .Select(i => new BatchDetail
            {
                BatchId = i,
                Name = $"Batch {i}",
                OrganizationId = $"org-{i % 2}",
                CreatedAtUtc = new DateTime(2026, 01, 01).AddDays(i)
            })
            .ToArray();
        _mockDataSvc
            .Setup(s => s.GetAllBatchesAcrossOrgsAsync())
            .ReturnsAsync(batches);

        var result = await _controller.AllBatches(page: 2, pageSize: 10);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminAllBatchesViewModel>(view.Model);
        Assert.Equal(2, model.CurrentPage);
        Assert.Equal(10, model.PageSize);
        Assert.Equal(11, model.TotalCount);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal("created", model.Sort);
        Assert.Equal("desc", model.Dir);
        Assert.Single(model.Batches);
        Assert.Equal("Batch 1", model.Batches[0].Name);
    }

    [Fact]
    public async Task AllBatches_SortsByFilesAscending()
    {
        var batches = new[]
        {
            new BatchDetail { BatchId = 1, Name = "Batch A", OrganizationId = "org-1", FileCount = 9, CreatedAtUtc = new DateTime(2026, 01, 01) },
            new BatchDetail { BatchId = 2, Name = "Batch B", OrganizationId = "org-1", FileCount = 2, CreatedAtUtc = new DateTime(2026, 01, 02) },
            new BatchDetail { BatchId = 3, Name = "Batch C", OrganizationId = "org-2", FileCount = 5, CreatedAtUtc = new DateTime(2026, 01, 03) }
        };
        _mockDataSvc
            .Setup(s => s.GetAllBatchesAcrossOrgsAsync())
            .ReturnsAsync(batches);

        var result = await _controller.AllBatches(page: 1, pageSize: 10, sort: "files", dir: "asc");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminAllBatchesViewModel>(view.Model);
        Assert.Equal("files", model.Sort);
        Assert.Equal("asc", model.Dir);
        Assert.Equal(2, model.Batches[0].FileCount);
        Assert.Equal(5, model.Batches[1].FileCount);
        Assert.Equal(9, model.Batches[2].FileCount);
    }

    [Fact]
    public async Task AllBatches_FiltersByOrganization()
    {
        var batches = new[]
        {
            new BatchDetail { BatchId = 1, Name = "Batch A", OrganizationId = "org-1", OrganizationName = "Org One", CreatedAtUtc = new DateTime(2026, 01, 01) },
            new BatchDetail { BatchId = 2, Name = "Batch B", OrganizationId = "org-2", OrganizationName = "Org Two", CreatedAtUtc = new DateTime(2026, 01, 02) },
            new BatchDetail { BatchId = 3, Name = "Batch C", OrganizationId = "org-1", OrganizationName = "Org One", CreatedAtUtc = new DateTime(2026, 01, 03) }
        };
        _mockDataSvc
            .Setup(s => s.GetAllBatchesAcrossOrgsAsync())
            .ReturnsAsync(batches);

        var result = await _controller.AllBatches(page: 1, pageSize: 10, orgId: "org-1");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminAllBatchesViewModel>(view.Model);
        Assert.Equal("org-1", model.SelectedOrgId);
        Assert.Equal(2, model.TotalCount);
        Assert.All(model.Batches, b => Assert.Equal("org-1", b.OrganizationId));
    }

    [Fact]
    public async Task AllUsers_ReturnsSortedPagedViewModel()
    {
        var users = new[]
        {
            new SuperAdminUserDetail { UserId = "1", UserName = "zeta", FullName = "Zeta User", Email = "zeta@x.com", OrganizationName = "Beta Org", IsGlobalAdmin = false },
            new SuperAdminUserDetail { UserId = "2", UserName = "alpha", FullName = "Alpha User", Email = "alpha@x.com", OrganizationName = "Acme Org", IsGlobalAdmin = false },
            new SuperAdminUserDetail { UserId = "3", UserName = "super", FullName = "Super Admin", Email = "super@x.com", OrganizationName = null, IsGlobalAdmin = true }
        };
        _mockDataSvc
            .Setup(s => s.GetAllUsersAsync())
            .ReturnsAsync(users);

        var result = await _controller.AllUsers(page: 1, pageSize: 10, sort: "org", dir: "asc");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminAllUsersViewModel>(view.Model);
        Assert.Equal(1, model.CurrentPage);
        Assert.Equal(10, model.PageSize);
        Assert.Equal(3, model.TotalCount);
        Assert.Equal(1, model.TotalPages);
        Assert.Equal("org", model.Sort);
        Assert.Equal("asc", model.Dir);
        Assert.Equal("Super Admin", model.Users[0].FullName);
        Assert.Equal("Alpha User", model.Users[1].FullName);
        Assert.Equal("Zeta User", model.Users[2].FullName);
    }

    [Fact]
    public async Task AllUsers_FiltersByOrganization()
    {
        var users = new[]
        {
            new SuperAdminUserDetail { UserId = "1", UserName = "zeta", FullName = "Zeta User", Email = "zeta@x.com", OrganizationId = "org-2", OrganizationName = "Beta Org", IsGlobalAdmin = false },
            new SuperAdminUserDetail { UserId = "2", UserName = "alpha", FullName = "Alpha User", Email = "alpha@x.com", OrganizationId = "org-1", OrganizationName = "Acme Org", IsGlobalAdmin = false },
            new SuperAdminUserDetail { UserId = "3", UserName = "super", FullName = "Super Admin", Email = "super@x.com", OrganizationId = null, OrganizationName = null, IsGlobalAdmin = true }
        };
        _mockDataSvc
            .Setup(s => s.GetAllUsersAsync())
            .ReturnsAsync(users);

        var result = await _controller.AllUsers(page: 1, pageSize: 10, sort: "name", dir: "asc", orgId: "org-1");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminAllUsersViewModel>(view.Model);
        Assert.Equal("org-1", model.SelectedOrgId);
        Assert.Single(model.Users);
        Assert.Equal("Alpha User", model.Users[0].FullName);
    }

    [Fact]
    public async Task GetAllBatches_ReturnsJsonArray()
    {
        var batches = new[]
        {
            new BatchDetail { BatchId = 1, Name = "Batch A", OrganizationId = "org-1" },
            new BatchDetail { BatchId = 2, Name = "Batch B", OrganizationId = "org-2" }
        };
        _mockDataSvc
            .Setup(s => s.GetAllBatchesAcrossOrgsAsync())
            .ReturnsAsync(batches);

        var result = await _controller.GetAllBatches();

        var json = Assert.IsType<JsonResult>(result);
        var returned = Assert.IsAssignableFrom<BatchDetail[]>(json.Value);
        Assert.Equal(2, returned.Length);
    }

    [Fact]
    public async Task CreateOrganization_DuplicateName_ReturnsDuplicateFlag()
    {
        var request = new CreateOrganizationRequest
        {
            Name = "Acme",
            Description = "Desc",
            AdminEmail = "admin@acme.com",
            AdminName = "Admin"
        };

        _mockSuperAdminSvc
            .Setup(s => s.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string?>()))
            .ThrowsAsync(new DuplicateOrganizationNameException("An organization named \"Acme\" already exists."));

        var result = await _controller.CreateOrganization(request);

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;

        var successProp = value.GetType().GetProperty("success");
        Assert.NotNull(successProp);
        Assert.False((bool)successProp!.GetValue(value)!);

        var duplicateProp = value.GetType().GetProperty("duplicateName");
        Assert.NotNull(duplicateProp);
        Assert.True((bool)duplicateProp!.GetValue(value)!);

        var errorProp = value.GetType().GetProperty("error");
        Assert.NotNull(errorProp);
        Assert.Equal("An organization named \"Acme\" already exists.", (string)errorProp!.GetValue(value)!);
    }

    [Fact]
    public async Task PromoteOrganization_WhenSuccessful_ReturnsSuccess()
    {
        _mockSuperAdminSvc
            .Setup(s => s.PromoteOrganizationAsync("org-1", "Acme"))
            .ReturnsAsync(new PromoteOrganizationResult
            {
                OrganizationName = "Acme",
                StripeProvisioned = true
            });

        var result = await _controller.PromoteOrganization(new PromoteOrganizationRequest
        {
            OrgId = "org-1",
            NewOrganizationName = "Acme"
        });

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        Assert.True((bool)value.GetType().GetProperty("success")!.GetValue(value)!);
        _mockSuperAdminSvc.Verify(s => s.PromoteOrganizationAsync("org-1", "Acme"), Times.Once);
    }
}
