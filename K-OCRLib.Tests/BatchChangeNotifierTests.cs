using System.Reactive.Linq;
using K_OCR.Services;

namespace K_OCRLib.Tests;

public class BatchChangeNotifierTests
{
    [Fact]
    public void Notify_PublishesOrgId()
    {
        using var notifier = new BatchChangeNotifier();
        var received = new List<string>();
        using var subscription = notifier.Changes.Subscribe(received.Add);

        notifier.Notify("org-123");

        Assert.Equal(["org-123"], received);
    }

    [Fact]
    public void Notify_IgnoresEmptyValues()
    {
        using var notifier = new BatchChangeNotifier();
        var received = new List<string>();
        using var subscription = notifier.Changes.Subscribe(received.Add);

        notifier.Notify(string.Empty);
        notifier.Notify(null!);
        notifier.Notify("org-456");

        Assert.Equal(["org-456"], received);
    }
}
