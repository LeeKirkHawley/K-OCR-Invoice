using System;
using System.Collections.Generic;
using System.Linq;
using K_OCR.Models;
using K_OCR.Security;

namespace K_OCR.Services;

/// <summary>
/// Scoped (per-circuit) state shared between <c>MainLayout</c> and page components.
/// Holds the currently open folder, the file list, and the selected file, and fires
/// an <see cref="OnChange"/> event so subscribers can call <c>StateHasChanged</c>.
/// </summary>
public class WorkspaceState
{
    // ── Public state ──────────────────────────────────────────────────────────

    public string? CurrentDirectory { get; private set; }

    public IReadOnlyList<FileListEntry> Files { get; private set; } = [];

    public string? SelectedFilePath { get; private set; }

    public FileListEntry? SelectedFile =>
        Files.FirstOrDefault(f => f.FilePath == SelectedFilePath);

    /// <summary>The invoice loaded or processed for the currently selected file.</summary>
    public InvoiceDto? SelectedInvoice { get; private set; }

    /// <summary>True while OCR is running for any file in this session.</summary>
    public bool IsOcrRunning { get; private set; }

    /// <summary>Per-file OCR progress message. Key = absolute file path.</summary>
    public Dictionary<string, string> OcrProgress { get; } = new();

    /// <summary>
    /// The field name currently selected in the validation panel (e.g. "VendorName").
    /// DocumentViewer watches this to highlight the matching bounding box.
    /// </summary>
    public string? SelectedFieldName { get; private set; }

    /// <summary>Short message shown in the top bar (e.g. "42 files · 3 processed").</summary>
    public string StatusMessage { get; private set; } = string.Empty;

    public string? TenantId { get; private set; }

    public string? TenantName { get; private set; }

    public string? CurrentUserEmail { get; private set; }

    public string? CurrentUserId { get; private set; }

    private IReadOnlyList<string> _roles = Array.Empty<string>();
    public IReadOnlyList<string> Roles => _roles;

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(CurrentUserEmail);

    public bool IsSuperAdmin => Roles.Contains(RoleNames.SuperAdmin);

    public bool IsOrganizationAdmin => IsSuperAdmin || Roles.Contains(RoleNames.OrganizationAdmin);

    public bool IsOrganizationValidator => Roles.Contains(RoleNames.OrganizationValidator);

    public bool IsOrganizationUser => Roles.Contains(RoleNames.OrganizationUser);

    /// <summary>The batch currently open in this session.</summary>
    public BatchSummary? CurrentBatch { get; private set; }

    /// <summary>True when the invoice panel has edits that have not been saved yet.</summary>
    public bool HasUnsavedEdits { get; private set; }

    // ── Event ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised (synchronously) whenever any state changes.
    /// Blazor components subscribe and call <c>InvokeAsync(StateHasChanged)</c>.
    /// </summary>
    public event Action? OnChange;

    // ── Mutators ──────────────────────────────────────────────────────────────

    /// <summary>Replace the entire directory / file list and clear the selection.</summary>
    public void SetDirectory(string? path, IReadOnlyList<FileListEntry> files, string? statusMessage = null)
    {
        CurrentDirectory  = path;
        Files             = files;
        SelectedFilePath  = null;
        SelectedInvoice   = null;
        SelectedFieldName = null;
        OcrProgress.Clear();
        StatusMessage    = statusMessage
            ?? (path is null ? string.Empty : BuildDefaultStatus(files));
        NotifyChange();
    }

    /// <summary>Set (or clear) the currently selected file and its cached invoice (if any).</summary>
    public void SelectFile(string? filePath, InvoiceDto? invoice = null)
    {
        SelectedFilePath  = filePath;
        SelectedInvoice   = invoice;
        SelectedFieldName = null;
        NotifyChange();
    }

    /// <summary>Set (or clear) the field currently highlighted in the validation panel.</summary>
    public void SetSelectedField(string? fieldName)
    {
        SelectedFieldName = fieldName;
        NotifyChange();
    }

    // ── File navigation ───────────────────────────────────────────────────────

    /// <summary>True when the selected file is not the first in the list.</summary>
    public bool CanNavigatePrev =>
        SelectedFilePath is not null
        && Files.Count > 0
        && Files[0].FilePath != SelectedFilePath;

    /// <summary>True when the selected file is not the last in the list.</summary>
    public bool CanNavigateNext =>
        SelectedFilePath is not null
        && Files.Count > 0
        && Files[^1].FilePath != SelectedFilePath;

    /// <summary>
    /// Move the selection forward (<paramref name="delta"/> = +1) or backward (-1).
    /// Clears the current invoice and field highlight, and returns the newly selected
    /// <see cref="FileListEntry"/>, or <c>null</c> if already at the boundary.
    /// </summary>
    public FileListEntry? NavigateFile(int delta)
    {
        if (Files.Count == 0 || SelectedFilePath is null) return null;
        var idx = Files.ToList().FindIndex(f => f.FilePath == SelectedFilePath);
        if (idx < 0) return null;
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= Files.Count) return null;
        var newFile       = Files[newIdx];
        SelectedFilePath  = newFile.FilePath;
        SelectedInvoice   = null;
        SelectedFieldName = null;
        NotifyChange();
        return newFile;
    }

    /// <summary>Update the invoice for the currently selected file (after OCR or cache load).</summary>
    public void SetSelectedInvoice(InvoiceDto? invoice)
    {
        SelectedInvoice = invoice;
        NotifyChange();
    }

    /// <summary>Replace one entry in the file list (e.g. after OCR completes).</summary>
    public void UpdateFileEntry(FileListEntry updated)
    {
        var list = Files.ToList();
        var idx  = list.FindIndex(f => f.FilePath == updated.FilePath);
        if (idx >= 0) list[idx] = updated;
        Files = list;
        StatusMessage = BuildDefaultStatus(Files);
        NotifyChange();
    }

    /// <summary>Mark OCR as running / idle, optionally updating the top-bar message.</summary>
    public void SetOcrRunning(bool running, string? statusMessage = null)
    {
        IsOcrRunning = running;
        if (statusMessage is not null) StatusMessage = statusMessage;
        NotifyChange();
    }

    /// <summary>Record a per-file OCR progress message and notify subscribers.</summary>
    public void SetOcrProgress(string filePath, string message)
    {
        OcrProgress[filePath] = message;
        NotifyChange();
    }

    /// <summary>Push an arbitrary status message to the top bar.</summary>
    public void SetStatus(string message)
    {
        StatusMessage = message;
        NotifyChange();
    }

    /// <summary>Capture tenant context and roles for the signed-in user.</summary>
    public void SetAuthentication(
        string? userId,
        string? tenantId,
        string? tenantName,
        string? userEmail,
        IEnumerable<string>? roles)
    {
        CurrentUserId    = string.IsNullOrWhiteSpace(userId)     ? null : userId.Trim();
        TenantId         = string.IsNullOrWhiteSpace(tenantId)   ? null : tenantId.Trim();
        TenantName       = string.IsNullOrWhiteSpace(tenantName) ? null : tenantName.Trim();
        CurrentUserEmail = string.IsNullOrWhiteSpace(userEmail)  ? null : userEmail.Trim();
        _roles = roles?
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        NotifyChange();
    }

    public void ClearAuthentication()
    {
        CurrentUserId    = null;
        TenantId         = null;
        TenantName       = null;
        CurrentUserEmail = null;
        _roles           = Array.Empty<string>();
        NotifyChange();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildDefaultStatus(IReadOnlyList<FileListEntry> files)
    {
        var processed = files.Count(f => f.IsProcessed);
        var suspect   = files.Count(f => f.HasSuspectFields);
        var parts = new List<string> { $"{files.Count} file{(files.Count == 1 ? "" : "s")}" };
        if (processed > 0) parts.Add($"{processed} processed");
        if (suspect   > 0) parts.Add($"{suspect} suspect");
        return string.Join(" · ", parts);
    }

    public void SetCurrentBatch(BatchSummary? batch)
    {
        CurrentBatch = batch;
        NotifyChange();
    }

    public void SetHasUnsavedEdits(bool value)
    {
        HasUnsavedEdits = value;
        NotifyChange();
    }

    private void NotifyChange() => OnChange?.Invoke();
}
