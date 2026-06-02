namespace K_OCRLib.Services;

public class DuplicateOrganizationNameException : Exception
{
    public DuplicateOrganizationNameException(string message) : base(message) { }
}
