using K_OCRLib.Models.Api.OrganizationAdmin;
using K_OCRLib.Models.Api.SuperAdmin;

namespace KOCRAsp.Models;

public sealed class AdminOrganizationDetailsViewModel
{
    public required OrganizationOverview Organization { get; init; }
    public int DeletedOrgRetentionDays { get; init; } = 14;
    public OrganizationUserOverview[] Users { get; init; } = [];
}
