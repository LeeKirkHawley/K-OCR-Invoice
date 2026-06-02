using K_OCRLib.Identity;

namespace K_OCRLib.Data;

/// <summary>
/// Provides tenant identity so <c>KOCRDbContext</c> can be routed to the
/// correct per-organisation SQLite file.
///
/// Lives in <c>K-OCRLib</c> so the context can depend on it without a circular
/// reference.  The concrete implementation (<c>TenantContext</c>) lives in the
/// web project and reads from the per-circuit <c>WorkspaceState</c>.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The organisation the authenticated user belongs to, or <c>null</c> for
    /// super-admins and unauthenticated contexts.
    /// </summary>
    string? OrganizationId { get; }

    /// <summary>
    /// The organisation name used to derive the per-org database file path via
    /// <c>IPathService.GetOrgDbPath</c>.  <c>null</c> for super-admins and
    /// unauthenticated contexts.
    /// </summary>
    string? OrganizationName { get; }

    /// <summary>
    /// The full <see cref="Organization"/> entity for the current tenant, or
    /// <c>null</c> for super-admins and unauthenticated contexts.
    /// </summary>
    Organization? Organization { get; }

    /// <summary>
    /// When <c>true</c> the user is a super-admin with no org scope.
    /// </summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// The Stripe customer ID for the org, used to report metered usage via Billing Meter events.
    /// <c>null</c> if billing has not been provisioned.
    /// </summary>
    string? StripeCustomerId { get; }

    /// <summary>
    /// The cached Stripe subscription status for the org (e.g. "active", "past_due", "canceled").
    /// Defaults to "none" when billing has not been provisioned.
    /// </summary>
    string StripeSubscriptionStatus { get; }

    /// <summary>
    /// When <c>true</c> the org is a guest organisation and is exempt from Stripe billing.
    /// </summary>
    bool IsGuestOrganization { get; }

    /// <summary>
    /// When <c>true</c> the org is a beta-test organisation and follows trial-style billing rules.
    /// </summary>
    bool IsBetaTestOrganization { get; }

    /// <summary>
    /// When <c>true</c> the org is running in a trial mode (guest or beta-test).
    /// </summary>
    bool IsTrialOrganization { get; }
}
