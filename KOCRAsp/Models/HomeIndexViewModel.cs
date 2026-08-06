using K_OCRLib.Models;

namespace KOCRAsp.Models;

public class HomeIndexViewModel
{
    public string OrgId { get; set; } = string.Empty;
    public string CurrentUserEmail { get; set; } = string.Empty;
    public List<BatchSummary> AvailableBatches { get; set; } = new();
    public BatchSummary? CurrentBatch { get; set; }
    public List<FileListEntry> Files { get; set; } = new();
    public int TotalFiles { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalPages { get; set; }
    public int MaxPagesPerInvoice { get; set; } = 20;
    public int MaxInvoicesPerBatch { get; set; } = 100;
    public bool IsBetaTestOrganization { get; set; }
    public bool IsOrgAdmin { get; set; }
    public bool ShowBetaWelcomeDialog { get; set; }
    public int BetaMaxOcrPages { get; set; }
    public int BetaUsedOcrPages { get; set; }
    public int BetaRemainingOcrPages { get; set; }
    public bool BetaOcrLimitExceeded { get; set; }

    public bool IsGuestOrganization { get; set; }
    public bool ShowGuestWelcomeDialog { get; set; }
    public int GuestMaxBatches { get; set; }
    public int GuestActiveBatchCount { get; set; }
    public int GuestRemainingBatches { get; set; }
    public bool GuestBatchLimitExceeded { get; set; }
    public int GuestMaxInvoicesPerBatch { get; set; }
    public int GuestMaxPagesPerInvoice { get; set; }
    public double MinConfidenceThreshold { get; set; } = 0.8;
}
