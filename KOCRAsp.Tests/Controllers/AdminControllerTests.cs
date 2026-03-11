using K_OCR.Models;
using KOCRAsp.Controllers;
using KOCRAsp.Models.Api.SuperAdmin;
using KOCRAsp.Services;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class AdminControllerTests
{
    private readonly Mock<ISuperAdminService> _mockSuperAdminSvc;
    private readonly Mock<ISuperAdminDataService> _mockDataSvc;
    private readonly Mock<ILogger<AdminController>> _mockLogger;
    private readonly AdminController _controller;

    public AdminControllerTests()
    {
        _mockSuperAdminSvc = new Mock<ISuperAdminService>();
        _mockDataSvc = new Mock<ISuperAdminDataService>();
        _mockLogger = new Mock<ILogger<AdminController>>();

        _controller = new AdminController(
            _mockSuperAdminSvc.Object,
            _mockDataSvc.Object,
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
}
