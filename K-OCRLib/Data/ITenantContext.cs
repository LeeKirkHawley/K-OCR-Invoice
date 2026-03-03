namespace K_OCR.Data;

/// <summary>
/// Provides tenant identity to <c>KOCRDbContext</c> for global query filtering
/// and automatic <c>OrganizationId</c> stamping on new records.
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
    /// When <c>true</c> the global query filter is bypassed so the user sees
    /// every tenant's data.
    /// </summary>
    bool IsSuperAdmin { get; }
}
