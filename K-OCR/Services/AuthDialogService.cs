namespace K_OCR.Services;

/// <summary>
/// Scoped (per-circuit) service that lets any component request the
/// Authentication dialog to open without a direct @ref dependency.
/// </summary>
public class AuthDialogService
{
    public event Action? OpenRequested;

    public void RequestOpen() => OpenRequested?.Invoke();
}
