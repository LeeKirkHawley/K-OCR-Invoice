namespace K_OCR.Security;

/// <summary>
/// Application-specific claim type URIs added to the Identity cookie by
/// <c>ApplicationUserClaimsPrincipalFactory</c>.  Use these constants whenever
/// reading or writing custom claims so strings stay in sync across the app.
/// </summary>
public static class AppClaimTypes
{
    /// <summary>The organisation (tenant) the signed-in user belongs to.</summary>
    public const string OrganizationId = "k-ocr:organization-id";

    /// <summary>Display name of the user's organisation, or "Global" for super-admins.</summary>
    public const string TenantName = "k-ocr:tenant-name";
}
