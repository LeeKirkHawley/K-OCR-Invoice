namespace K_OCR.Services;

/// <summary>
/// Scoped (per-circuit) service that lets any component request the Super Admin
/// dialog to open without needing a direct @ref to the dialog component.
/// </summary>
public class SuperAdminDialogService
{
    public event Action? OpenRequested;
    public void RequestOpen() => OpenRequested?.Invoke();
}
