using Moq;
using OCRQueue.Abstractions;
using OCRQueue.Models;
using OCRQueue.Services;

namespace OCRQueue.Tests;

public class OcrJobQueueTests
{
    private Mock<IOcrQueueStatsNotifier> _mockNotifier;
    private OcrJobQueue _queue;

    public OcrJobQueueTests()
    {
        _mockNotifier = new Mock<IOcrQueueStatsNotifier>();
        _queue = new OcrJobQueue(_mockNotifier.Object);
    }

    [Fact]
    public async Task EnqueueAsync_AddsJobToQueue()
    {
        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        await _queue.EnqueueAsync(job);

        var dequeuedJob = await _queue.DequeueAsync();
        Assert.Equal(job.JobId, dequeuedJob.JobId);
        Assert.Equal("TestOrg", dequeuedJob.OrgName);
    }

    [Fact]
    public async Task EnqueueAsync_IncrementsTotalCount()
    {
        Assert.Equal(0, _queue.TotalCount);

        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        await _queue.EnqueueAsync(job);

        Assert.Equal(1, _queue.TotalCount);
    }

    [Fact]
    public async Task EnqueueAsync_TracksPerOrgCount()
    {
        var job1 = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "OrgA",
            FilePath: "/path/to/file1.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        var job2 = new OcrJob(
            JobId: 2,
            InvoiceId: 101,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "OrgA",
            FilePath: "/path/to/file2.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        var job3 = new OcrJob(
            JobId: 3,
            InvoiceId: 102,
            BatchId: 11,
            OrgId: "org-2",
            OrgName: "OrgB",
            FilePath: "/path/to/file3.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        await _queue.EnqueueAsync(job1);
        await _queue.EnqueueAsync(job2);
        await _queue.EnqueueAsync(job3);

        Assert.Equal(3, _queue.TotalCount);
        Assert.Equal(2, _queue.CountPerOrg["org-1"]);
        Assert.Equal(1, _queue.CountPerOrg["org-2"]);
    }

    [Fact]
    public async Task EnqueueAsync_PublishesStats()
    {
        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        await _queue.EnqueueAsync(job);

        _mockNotifier.Verify(
            n => n.Publish(It.IsAny<QueueStats>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyJobComplete_DecrementsCount()
    {
        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        await _queue.EnqueueAsync(job);
        Assert.Equal(1, _queue.TotalCount);

        _queue.NotifyJobComplete(job);
        Assert.Equal(0, _queue.TotalCount);
    }

    [Fact]
    public async Task NotifyJobComplete_RemovesOrgFromCountWhenZero()
    {
        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        await _queue.EnqueueAsync(job);
        Assert.True(_queue.CountPerOrg.ContainsKey("org-1"));

        _queue.NotifyJobComplete(job);
        Assert.False(_queue.CountPerOrg.ContainsKey("org-1"));
    }

    [Fact]
    public async Task InvoicesPerOrg_TracksFileNamesPerOrg()
    {
        var job1 = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "OrgA",
            FilePath: "/path/to/invoice1.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        var job2 = new OcrJob(
            JobId: 2,
            InvoiceId: 101,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "OrgA",
            FilePath: "/path/to/invoice2.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        await _queue.EnqueueAsync(job1);
        await _queue.EnqueueAsync(job2);

        var invoices = _queue.InvoicesPerOrg["org-1"];
        Assert.Contains("invoice1.pdf", invoices);
        Assert.Contains("invoice2.pdf", invoices);
        Assert.Equal(2, invoices.Count);
    }

    [Fact]
    public async Task DequeueAsync_DoesNotDecrementOutstandingCounts()
    {
        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        await _queue.EnqueueAsync(job);
        Assert.Equal(1, _queue.TotalCount);
        Assert.Equal(1, _queue.CountPerOrg["org-1"]);

        var dequeued = await _queue.DequeueAsync();

        Assert.Equal(job.JobId, dequeued.JobId);
        Assert.Equal(1, _queue.TotalCount);
        Assert.Equal(1, _queue.CountPerOrg["org-1"]);
    }

    [Fact]
    public void NotifyJobComplete_DoesNotAllowNegativeTotalCount()
    {
        var job = new OcrJob(
            JobId: 999,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        _queue.NotifyJobComplete(job);

        Assert.Equal(0, _queue.TotalCount);
        Assert.False(_queue.CountPerOrg.ContainsKey("org-1"));
    }

    [Fact]
    public void Dispose_CompletesChannel()
    {
        _queue.Dispose();
        // No exception should be thrown when disposing a disposed queue
        _queue.Dispose();
    }
}
