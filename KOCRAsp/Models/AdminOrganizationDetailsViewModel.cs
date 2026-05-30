using K_OCR.Models.Api.SuperAdmin;

namespace KOCRAsp.Models;

public sealed class AdminOrganizationDetailsViewModel
{
    public required OrganizationOverview Organization { get; init; }
    public int DeletedOrgRetentionDays { get; init; } = 14;
}
