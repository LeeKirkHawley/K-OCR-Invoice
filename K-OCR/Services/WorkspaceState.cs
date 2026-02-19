using K_OCR.Models;

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

    /// <summary>Short message shown in the top bar (e.g. "42 files · 3 processed").</summary>
    public string StatusMessage { get; private set; } = string.Empty;

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
        StatusMessage     = statusMessage
            ?? (path is null ? string.Empty
                             : BuildDefaultStatus(files));
        NotifyChange();
    }

    /// <summary>Set (or clear) the currently selected file.</summary>
    public void SelectFile(string? filePath)
    {
        SelectedFilePath = filePath;
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

    /// <summary>Push an arbitrary status message to the top bar.</summary>
    public void SetStatus(string message)
    {
        StatusMessage = message;
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

    private void NotifyChange() => OnChange?.Invoke();
}
