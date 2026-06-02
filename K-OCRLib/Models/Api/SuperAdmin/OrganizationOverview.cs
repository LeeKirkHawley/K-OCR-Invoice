namespace K_OCRLib.Models.Api.SuperAdmin;

public class OrganizationOverview
{
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsGuestOrganization { get; set; }
    public bool IsBetaTestOrganization { get; set; }
    public int BetaMaxOcrPages { get; set; }
    public DateTime? MarkedForDeletionAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int UserCount { get; set; }
    public string? StripeCustomerId { get; set; }
    public string StripeSubscriptionStatus { get; set; } = "none";
}
