using K_OCR.Models;

namespace KOCRAsp.Models;

public sealed class AdminAllUsersViewModel
{
    public IReadOnlyList<SuperAdminUserDetail> Users { get; init; } = [];
    public IReadOnlyList<AdminOrgFilterOption> OrgOptions { get; init; } = [];
    public string? SelectedOrgId { get; init; }
    public int CurrentPage { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public string Sort { get; init; } = "name";
    public string Dir { get; init; } = "asc";
}
