using K_OCR.Models;
using K_OCR.Services;
using KOCRAsp.Controllers;
using K_OCR.Models.Api.SuperAdmin;
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
        _mockDataSvc
            .Setup(s => s.GetAllBatchesAcrossOrgsAsync())
            .ReturnsAsync(Array.Empty<BatchDetail>());

        var result = await _controller.Index();

        Assert.IsType<ViewResult>(result);
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
