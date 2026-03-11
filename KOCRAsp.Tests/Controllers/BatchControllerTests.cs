using K_OCR.Models;
using K_OCR.Services;
using KOCRAsp.Controllers;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class BatchControllerTests
{
    private readonly Mock<IBatchService> _mockBatchSvc;
    private readonly Mock<ILogger<BatchController>> _mockLogger;
    private readonly BatchController _controller;

    private const string UserId = "user-id-123";
    private const string OrgId = "org-id-123";
    private const string OrgName = "TestOrg";

    public BatchControllerTests()
    {
        _mockBatchSvc = new Mock<IBatchService>();
        _mockLogger = new Mock<ILogger<BatchController>>();
        _controller = new BatchController(_mockBatchSvc.Object, _mockLogger.Object);
        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext(
            userId: UserId, orgId: OrgId, orgName: OrgName);
        _controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
    }

    [Fact]
    public async Task Index_ReturnsViewWithBatches()
    {
        var batches = new[]
        {
            new BatchSummary { BatchId = 1, Name = "Batch A", OrganizationId = OrgId },
            new BatchSummary { BatchId = 2, Name = "Batch B", OrganizationId = OrgId }
        };
        _mockBatchSvc
            .Setup(s => s.GetBatchesForOrgAsync(OrgId))
            .ReturnsAsync(batches);

        var result = await _controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IList<BatchSummary>>(viewResult.Model);
        Assert.Equal(2, model.Count);
    }

    [Fact]
    public async Task Create_POST_ValidRequest_RedirectsToIndex()
    {
        _mockBatchSvc
            .Setup(s => s.CreateBatchAsync(It.IsAny<CreateBatchRequest>()))
            .ReturnsAsync(CreateBatchResult.Ok(42));

        var result = await _controller.Create("My Batch");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(BatchController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task Delete_POST_ValidId_ReturnsOkJson()
    {
        _mockBatchSvc
            .Setup(s => s.DeleteBatchAsync(1, UserId))
            .Returns(Task.CompletedTask);

        var result = await _controller.Delete(1);

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        var successProp = value.GetType().GetProperty("success");
        Assert.NotNull(successProp);
        Assert.True((bool)successProp.GetValue(value)!);
    }

    [Fact]
    public async Task Lock_POST_ValidId_ReturnsSuccess()
    {
        _mockBatchSvc
            .Setup(s => s.TryAcquireBatchLockAsync(1, UserId))
            .ReturnsAsync(AcquireLockResult.Ok());

        var result = await _controller.Lock(1);

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        var successProp = value.GetType().GetProperty("success");
        Assert.NotNull(successProp);
        Assert.True((bool)successProp.GetValue(value)!);
    }

    [Fact]
    public async Task Unlock_POST_ValidId_ReturnsSuccess()
    {
        _mockBatchSvc
            .Setup(s => s.ReleaseBatchLockAsync(1, UserId))
            .Returns(Task.CompletedTask);

        var result = await _controller.Unlock(1);

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        var successProp = value.GetType().GetProperty("success");
        Assert.NotNull(successProp);
        Assert.True((bool)successProp.GetValue(value)!);
    }
}
