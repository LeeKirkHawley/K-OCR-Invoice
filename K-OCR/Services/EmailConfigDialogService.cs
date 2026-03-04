namespace K_OCR.Services;

/// <summary>
/// Scoped (per-circuit) service that lets the Super Admin dialog request the
/// Email Configuration dialog to open.  No other caller can open it because
/// the service is only injected into SuperAdmin.razor (to fire it) and
/// EmailConfig.razor (to listen).
/// </summary>
public class EmailConfigDialogService
{
    public event Action? OpenRequested;
    public void RequestOpen() => OpenRequested?.Invoke();
}
