namespace K_OCR.Services;

/// <summary>
/// Scoped (per-circuit) service that lets any component request the Settings
/// dialog to open without needing a direct @ref to the dialog component.
/// </summary>
public class SettingsDialogService
{
    public event Action? OpenRequested;
    public void RequestOpen() => OpenRequested?.Invoke();
}
