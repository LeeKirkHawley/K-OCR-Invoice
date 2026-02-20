namespace K_OCR.Services;

public enum ToastLevel { Info, Success, Warning, Error }

public record ToastMessage(string Text, ToastLevel Level, Guid Id);

/// <summary>
/// Scoped per-circuit service that raises <see cref="OnToast"/> whenever a
/// notification should be displayed.  <c>ToastContainer.razor</c> subscribes
/// and renders the stacked toasts; they auto-dismiss after 5 seconds.
/// </summary>
public class ToastService
{
    public event Action<ToastMessage>? OnToast;

    public void ShowInfo(string message)    => Publish(message, ToastLevel.Info);
    public void ShowSuccess(string message) => Publish(message, ToastLevel.Success);
    public void ShowWarning(string message) => Publish(message, ToastLevel.Warning);
    public void ShowError(string message)   => Publish(message, ToastLevel.Error);

    private void Publish(string text, ToastLevel level) =>
        OnToast?.Invoke(new ToastMessage(text, level, Guid.NewGuid()));
}
