using K_OCRLib.Models;

namespace KOCRAsp.Models;

public sealed class AdminAllBatchesViewModel
{
    public IReadOnlyList<BatchDetail> Batches { get; init; } = [];
    public IReadOnlyList<AdminOrgFilterOption> OrgOptions { get; init; } = [];
    public string? SelectedOrgId { get; init; }
    public int CurrentPage { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public string Sort { get; init; } = "created";
    public string Dir { get; init; } = "desc";
}
