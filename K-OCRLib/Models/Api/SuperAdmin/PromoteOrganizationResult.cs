namespace K_OCRLib.Models.Api.SuperAdmin;

public sealed class PromoteOrganizationResult
{
    public bool StripeProvisioned { get; set; }
    public string? StripeProvisioningError { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
}

