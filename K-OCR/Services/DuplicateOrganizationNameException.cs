namespace K_OCR.Services;

/// <summary>
/// Thrown when a new organization name collides with an existing one
/// (either as a display name or as a sanitized folder name).
/// </summary>
public class DuplicateOrganizationNameException : Exception
{
    public DuplicateOrganizationNameException(string message) : base(message) { }
}
