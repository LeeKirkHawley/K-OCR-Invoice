using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace KOCRAsp.Services;

public interface IBatchChangeNotifier
{
    IObservable<string> Changes { get; }
    void Notify(string orgId);
}

/// <summary>
/// Singleton Rx subject that emits an orgId whenever that org's batch list changes.
/// Consumers (e.g. BatchChangeDispatcher) subscribe to <see cref="Changes"/> to react.
/// </summary>
public sealed class BatchChangeNotifier : IBatchChangeNotifier, IDisposable
{
    private readonly Subject<string> _subject = new();

    public IObservable<string> Changes => _subject.AsObservable();

    public void Notify(string orgId)
    {
        if (!string.IsNullOrEmpty(orgId))
            _subject.OnNext(orgId);
    }

    public void Dispose() => _subject.Dispose();
}
