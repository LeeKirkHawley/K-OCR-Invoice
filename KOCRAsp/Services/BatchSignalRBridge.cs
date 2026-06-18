using KOCRAsp.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using K_OCRLib.Services;

namespace KOCRAsp.Services;

/// <summary>
/// Hosted service that bridges IBatchChangeNotifier (Rx subject) to SignalR.
/// Emits a "BatchChanged" message to the org-specific group so clients can
/// refresh batch lists and file lists when a producing component calls
/// IBatchChangeNotifier.Notify(orgId).
/// </summary>
public sealed class BatchSignalRBridge : IHostedService, IDisposable
{
    private readonly IHubContext<OcrHub> _hubContext;
    private readonly IBatchChangeNotifier _batchNotifier;
    private readonly ILogger<BatchSignalRBridge> _logger;
    private IDisposable? _subscription;

    public BatchSignalRBridge(
        IHubContext<OcrHub> hubContext,
        IBatchChangeNotifier batchNotifier,
        ILogger<BatchSignalRBridge> logger)
    {
        _hubContext = hubContext;
        _batchNotifier = batchNotifier;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to batch change notifications and forward to SignalR.
        _subscription = _batchNotifier.Changes.Subscribe(
            onNext: orgId => OnBatchChanged(orgId),
            onError: ex => _logger.LogError(ex, "[BatchSignalR] Changes stream faulted."));

        _logger.LogInformation("[BatchSignalR] Bridge started — subscribed to IBatchChangeNotifier.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _logger.LogInformation("[BatchSignalR] Bridge stopped.");
        return Task.CompletedTask;
    }

    private void OnBatchChanged(string orgId)
    {
        if (string.IsNullOrWhiteSpace(orgId)) return;

        // Fire-and-forget — best-effort push to org-specific group
        var payload = new { orgId };
        _ = _hubContext.Clients
            .Group($"OcrCompleted_{orgId}")
            .SendAsync("BatchChanged", payload)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogWarning(t.Exception, "[BatchSignalR] Failed to send BatchChanged for org {OrgId}", orgId);
            });

        _logger.LogDebug("[BatchSignalR] Pushed BatchChanged for org {OrgId}", orgId);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
