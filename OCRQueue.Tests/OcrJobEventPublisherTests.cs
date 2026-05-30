using OCRQueue.Abstractions;
using OCRQueue.Models;
using OCRQueue.Services;

namespace OCRQueue.Tests;

public class OcrJobEventPublisherTests
{
    [Fact]
    public void OnJobEnqueued_EmitsJobToSubscribers()
    {
        var publisher = new OcrJobEventPublisher();
        var enqueuedJobs = new List<OcrJob>();

        publisher.JobEnqueued.Subscribe(job => enqueuedJobs.Add(job));

        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        publisher.OnJobEnqueued(job);

        Assert.Single(enqueuedJobs);
        Assert.Equal(1, enqueuedJobs[0].JobId);
    }

    [Fact]
    public void OnJobCompleted_EmitsJobToSubscribers()
    {
        var publisher = new OcrJobEventPublisher();
        var completedJobs = new List<OcrJob>();

        publisher.JobCompleted.Subscribe(job => completedJobs.Add(job));

        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        publisher.OnJobCompleted(job);

        Assert.Single(completedJobs);
        Assert.Equal(1, completedJobs[0].JobId);
    }

    [Fact]
    public void OnJobFailed_EmitsFailedEventToSubscribers()
    {
        var publisher = new OcrJobEventPublisher();
        var failedEvents = new List<OcrJobFailedEvent>();

        publisher.JobFailed.Subscribe(evt => failedEvents.Add(evt));

        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        var error = "Something went wrong";
        publisher.OnJobFailed(job, error);

        Assert.Single(failedEvents);
        Assert.Equal(1, failedEvents[0].Job.JobId);
        Assert.Equal(error, failedEvents[0].Error);
    }

    [Fact]
    public void JobCompleted_MultipleSubscribers_AllReceiveEvent()
    {
        var publisher = new OcrJobEventPublisher();
        var subscriber1 = new List<OcrJob>();
        var subscriber2 = new List<OcrJob>();

        publisher.JobCompleted.Subscribe(j => subscriber1.Add(j));
        publisher.JobCompleted.Subscribe(j => subscriber2.Add(j));

        var job = new OcrJob(
            JobId: 1,
            InvoiceId: 100,
            BatchId: 10,
            OrgId: "org-1",
            OrgName: "TestOrg",
            FilePath: "/path/to/file.pdf",
            WorkflowKey: "Default",
            QueuedAtUtc: DateTime.UtcNow);

        publisher.OnJobCompleted(job);

        Assert.Single(subscriber1);
        Assert.Single(subscriber2);
    }

    [Fact]
    public void Dispose_CompletesObservables()
    {
        var publisher = new OcrJobEventPublisher();
        var enqueuedFinished = false;
        var completedFinished = false;
        var failedFinished = false;

        publisher.JobEnqueued.Subscribe(
            onNext: _ => { },
            onCompleted: () => { enqueuedFinished = true; });

        publisher.JobCompleted.Subscribe(
            onNext: _ => { },
            onCompleted: () => { completedFinished = true; });

        publisher.JobFailed.Subscribe(
            onNext: _ => { },
            onCompleted: () => { failedFinished = true; });

        publisher.Dispose();

        Assert.True(enqueuedFinished);
        Assert.True(completedFinished);
        Assert.True(failedFinished);
    }
}
