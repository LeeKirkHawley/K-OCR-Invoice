using System.ComponentModel.DataAnnotations;

namespace K_OCR.Identity;

public class Organization
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsGuestOrganization { get; set; }
    public bool IsBetaTestOrganization { get; set; }
    public int BetaMaxOcrPages { get; set; } = 500;

    public DateTime? MarkedForDeletionAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<UserOrganizationMembership> UserMemberships { get; set; } =
        new List<UserOrganizationMembership>();

    // Stripe billing
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? StripeSubscriptionItemId { get; set; }
    public string? StripePriceId { get; set; }
    public string StripeSubscriptionStatus { get; set; } = "none";
}
