namespace K_OCR.Services;

/// <summary>
/// Scoped (per-circuit) service that lets any component request the Organization
/// Users dialog to open without needing a direct @ref to the dialog component.
/// </summary>
public class OrgUsersDialogService
{
    public event Action? OpenRequested;
    public void RequestOpen() => OpenRequested?.Invoke();
}
