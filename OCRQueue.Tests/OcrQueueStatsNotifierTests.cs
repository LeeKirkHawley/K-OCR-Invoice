using OCRQueue.Models;
using OCRQueue.Services;

namespace OCRQueue.Tests;

public class OcrQueueStatsNotifierTests
{
    [Fact]
    public void Publish_EmitsStatsToObservers()
    {
        var notifier = new OcrQueueStatsNotifier();
        var receivedStats = new List<QueueStats>();

        notifier.Stats.Subscribe(stats => receivedStats.Add(stats));

        var stats1 = new QueueStats(
            TotalQueued: 5,
            PerOrgCount: new Dictionary<string, int> { { "org-1", 3 }, { "org-2", 2 } },
            PerOrgInvoices: new Dictionary<string, IReadOnlyList<string>>());

        notifier.Publish(stats1);

        Assert.Single(receivedStats);
        Assert.Equal(5, receivedStats[0].TotalQueued);
        Assert.Equal(3, receivedStats[0].PerOrgCount["org-1"]);
    }

    [Fact]
    public void Publish_MultipleSubscribers_AllReceiveUpdates()
    {
        var notifier = new OcrQueueStatsNotifier();
        var subscriber1 = new List<QueueStats>();
        var subscriber2 = new List<QueueStats>();

        notifier.Stats.Subscribe(s => subscriber1.Add(s));
        notifier.Stats.Subscribe(s => subscriber2.Add(s));

        var stats = new QueueStats(
            TotalQueued: 1,
            PerOrgCount: new Dictionary<string, int>(),
            PerOrgInvoices: new Dictionary<string, IReadOnlyList<string>>());

        notifier.Publish(stats);

        Assert.Single(subscriber1);
        Assert.Single(subscriber2);
    }

    [Fact]
    public void Publish_EmitsMultipleUpdates()
    {
        var notifier = new OcrQueueStatsNotifier();
        var receivedStats = new List<QueueStats>();

        notifier.Stats.Subscribe(stats => receivedStats.Add(stats));

        var stats1 = new QueueStats(1, new Dictionary<string, int>(), new Dictionary<string, IReadOnlyList<string>>());
        var stats2 = new QueueStats(2, new Dictionary<string, int>(), new Dictionary<string, IReadOnlyList<string>>());

        notifier.Publish(stats1);
        notifier.Publish(stats2);

        Assert.Equal(2, receivedStats.Count);
        Assert.Equal(1, receivedStats[0].TotalQueued);
        Assert.Equal(2, receivedStats[1].TotalQueued);
    }

    [Fact]
    public void Dispose_DisposesSubject()
    {
        var notifier = new OcrQueueStatsNotifier();

        // Should not throw
        notifier.Dispose();

        // Verify that disposing again is safe
        notifier.Dispose();
    }
}
