using System;

namespace K_OCR.Models.Api.SuperAdmin;

public class OrganizationOverview
{
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int UserCount { get; set; }
}
