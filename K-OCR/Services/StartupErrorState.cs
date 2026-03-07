namespace K_OCR.Services;

public class StartupErrorState
{
    public string? ErrorMessage { get; private set; }
    public bool HasError => ErrorMessage is not null;
    public void SetError(string message) => ErrorMessage = message;
}
