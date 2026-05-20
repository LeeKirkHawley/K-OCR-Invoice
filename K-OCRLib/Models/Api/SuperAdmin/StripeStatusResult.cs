namespace K_OCR.Models.Api.SuperAdmin;

/// <summary>
/// Result of a Stripe subscription status sync attempt.
/// </summary>
/// <param name="IsApplicable">
/// <c>false</c> when the org is never Stripe-billed (guest org) and no status check was performed.
/// <c>true</c> for all real orgs — even when Stripe is unreachable or unconfigured.
/// </param>
/// <param name="Status">
/// The subscription status string (e.g. "active", "trialing", "canceled", "none", "error").
/// Empty string when <see cref="IsApplicable"/> is <c>false</c>.
/// </param>
public record StripeStatusResult(bool IsApplicable, string Status)
{
    /// <summary>Sentinel for guest orgs that are never Stripe-billed.</summary>
    public static StripeStatusResult NotApplicable => new(false, string.Empty);
}
