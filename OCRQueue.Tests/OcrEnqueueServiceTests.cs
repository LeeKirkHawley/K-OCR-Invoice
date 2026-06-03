using K_OCRLib.Models;
using Moq;
using OCRQueue.Abstractions;
using OCRQueue.Models;
using OCRQueue.Services;

namespace OCRQueue.Tests;

public class OcrEnqueueServiceTests
{
    private Mock<IOcrQueueRepository> _mockRepository;
    private Mock<IOcrJobQueue> _mockQueue;
    private Mock<IOcrJobEventPublisher> _mockEventPublisher;
    private OcrEnqueueService _service;

    public OcrEnqueueServiceTests()
    {
        _mockRepository = new Mock<IOcrQueueRepository>();
        _mockQueue = new Mock<IOcrJobQueue>();
        _mockEventPublisher = new Mock<IOcrJobEventPublisher>();
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<OcrEnqueueService>>();

        _service = new OcrEnqueueService(_mockRepository.Object, _mockQueue.Object, _mockEventPublisher.Object, mockLogger.Object);
    }

    [Fact]
    public async Task EnqueueFilesAsync_CreatesJobsInRepository()
    {
        var files = new List<(string, int)>
        {
            ("/path/file1.pdf", 100),
            ("/path/file2.pdf", 101),
        };

        _mockRepository.Setup(r => r.CreateJobAsync(It.IsAny<OcrJob>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OcrJob job, string orgName, CancellationToken ct) =>
            {
                var entity = new OcrJobRecord
                {
                    Id = 1,
                    InvoiceId = job.InvoiceId,
                    BatchId = job.BatchId,
                    OrgId = job.OrgId,
                    OrgName = orgName,
                    FilePath = job.FilePath,
                    WorkflowKey = job.WorkflowKey,
                    Status = OcrJobStatus.Queued,
                    QueuedAtUtc = job.QueuedAtUtc,
                };
                return entity;
            });

        var result = await _service.EnqueueFilesAsync(
            files, batchId: 10, orgId: "org-1", orgName: "TestOrg",
            workflowKey: "Default");

        Assert.Equal(2, result);
        _mockRepository.Verify(
            r => r.CreateJobAsync(It.IsAny<OcrJob>(), "TestOrg", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task EnqueueFilesAsync_EnqueuesJobsAfterPersisting()
    {
        var files = new List<(string, int)> { ("/path/file.pdf", 100) };

        _mockRepository.Setup(r => r.CreateJobAsync(It.IsAny<OcrJob>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OcrJob job, string orgName, CancellationToken ct) =>
            {
                var entity = new OcrJobRecord
                {
                    Id = 1,
                    InvoiceId = job.InvoiceId,
                    BatchId = job.BatchId,
                    OrgId = job.OrgId,
                    OrgName = orgName,
                    FilePath = job.FilePath,
                    WorkflowKey = job.WorkflowKey,
                    Status = OcrJobStatus.Queued,
                    QueuedAtUtc = job.QueuedAtUtc,
                };
                return entity;
            });

        await _service.EnqueueFilesAsync(
            files, batchId: 10, orgId: "org-1", orgName: "TestOrg",
            workflowKey: "Default");

        _mockQueue.Verify(
            q => q.EnqueueAsync(It.IsAny<OcrJob>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockEventPublisher.Verify(
            p => p.OnJobEnqueued(It.IsAny<OcrJob>()),
            Times.Once);
    }

    [Fact]
    public async Task EnqueueFilesAsync_UsesPersistedJobIdWhenEnqueueing()
    {
        var files = new List<(string, int)> { ("/path/file.pdf", 100) };

        _mockRepository
            .Setup(r => r.CreateJobAsync(It.IsAny<OcrJob>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OcrJob job, string orgName, CancellationToken ct) =>
                new OcrJobRecord
                {
                    Id = 42,
                    InvoiceId = job.InvoiceId,
                    BatchId = job.BatchId,
                    OrgId = job.OrgId,
                    OrgName = orgName,
                    FilePath = job.FilePath,
                    WorkflowKey = job.WorkflowKey,
                    Status = OcrJobStatus.Queued,
                    QueuedAtUtc = job.QueuedAtUtc,
                });

        await _service.EnqueueFilesAsync(
            files, batchId: 10, orgId: "org-1", orgName: "TestOrg",
            workflowKey: "Default");

        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<OcrJob>(j =>
                j.JobId == 42 &&
                j.InvoiceId == 100 &&
                j.BatchId == 10 &&
                j.OrgId == "org-1" &&
                j.OrgName == "TestOrg" &&
                j.FilePath == "/path/file.pdf" &&
                j.WorkflowKey == "Default"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueFilesAsync_ReturnsCountOfSuccessfullyEnqueuedJobs()
    {
        var files = new List<(string, int)>
        {
            ("/path/file1.pdf", 100),
            ("/path/file2.pdf", 101),
        };

        _mockRepository.Setup(r => r.CreateJobAsync(It.IsAny<OcrJob>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OcrJob job, string orgName, CancellationToken ct) =>
            {
                var entity = new OcrJobRecord
                {
                    Id = 1,
                    InvoiceId = job.InvoiceId,
                    BatchId = job.BatchId,
                    OrgId = job.OrgId,
                    OrgName = orgName,
                    FilePath = job.FilePath,
                    WorkflowKey = job.WorkflowKey,
                    Status = OcrJobStatus.Queued,
                    QueuedAtUtc = job.QueuedAtUtc,
                };
                return entity;
            });

        var result = await _service.EnqueueFilesAsync(
            files, batchId: 10, orgId: "org-1", orgName: "TestOrg",
            workflowKey: "Default");

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task EnqueueFilesAsync_ContinuesOnRepositoryError()
    {
        var files = new List<(string, int)>
        {
            ("/path/file1.pdf", 100),
            ("/path/file2.pdf", 101),
        };

        var callCount = 0;
        _mockRepository.Setup(r => r.CreateJobAsync(It.IsAny<OcrJob>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(async (OcrJob job, string orgName, CancellationToken ct) =>
            {
                if (callCount == 1)
                    throw new Exception("Database error");

                var entity = new OcrJobRecord
                {
                    Id = 2,
                    InvoiceId = job.InvoiceId,
                    BatchId = job.BatchId,
                    OrgId = job.OrgId,
                    OrgName = orgName,
                    FilePath = job.FilePath,
                    WorkflowKey = job.WorkflowKey,
                    Status = OcrJobStatus.Queued,
                    QueuedAtUtc = job.QueuedAtUtc,
                };
                await Task.CompletedTask;
                return entity;
            });

        var result = await _service.EnqueueFilesAsync(
            files, batchId: 10, orgId: "org-1", orgName: "TestOrg",
            workflowKey: "Default");

        // One job failed, one succeeded
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task EnqueueFilesAsync_WithEmptyFileList_ReturnsZero()
    {
        var files = new List<(string, int)>();

        var result = await _service.EnqueueFilesAsync(
            files, batchId: 10, orgId: "org-1", orgName: "TestOrg",
            workflowKey: "Default");

        Assert.Equal(0, result);
        _mockRepository.Verify(
            r => r.CreateJobAsync(It.IsAny<OcrJob>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
