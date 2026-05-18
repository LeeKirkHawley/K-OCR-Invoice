namespace K_OCR.Services;

public class DuplicateOrganizationNameException : Exception
{
    public DuplicateOrganizationNameException(string message) : base(message) { }
}
