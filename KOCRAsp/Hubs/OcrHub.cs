using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KOCRAsp.Hubs;

/// <summary>
/// SignalR hub for real-time OCR queue and invoice completion notifications.
/// <list type="bullet">
///   <item>
///     <term><c>QueueMonitors</c></term>
///     <description>
///       Group that receives <c>QueueStatsUpdated</c> messages whenever queue depth changes.
///       Join by calling <c>JoinQueueMonitor()</c>.
///     </description>
///   </item>
///   <item>
///     <term><c>OcrCompleted_{orgId}</c></term>
///     <description>
///       Per-org group that receives <c>InvoiceOcrCompleted</c> messages when a job finishes.
///       Join by calling <c>JoinOrgGroup(orgId)</c>.
///     </description>
///   </item>
/// </list>
/// </summary>
[Authorize]
public class OcrHub : Hub
{
    /// <summary>
    /// Adds the caller to the <c>QueueMonitors</c> group so they receive
    /// <c>QueueStatsUpdated</c> pushes.
    /// </summary>
    public Task JoinQueueMonitor() =>
        Groups.AddToGroupAsync(Context.ConnectionId, "QueueMonitors");

    /// <summary>
    /// Adds the caller to the org-specific group so they receive
    /// <c>InvoiceOcrCompleted</c> pushes for their organisation.
    /// </summary>
    /// <param name="orgId">The ASP.NET Identity organisation ID (GUID string).</param>
    public Task JoinOrgGroup(string orgId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"OcrCompleted_{orgId}");
}
