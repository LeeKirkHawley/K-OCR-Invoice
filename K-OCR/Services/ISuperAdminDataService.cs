using K_OCR.Models;

namespace K_OCR.Services;

/// <summary>
/// Provides cross-org data access for super-admin operations by enumerating
/// each organisation's per-org SQLite database.  Do NOT inject this into
/// regular org-user code paths — use <see cref="IBatchService"/> instead.
/// </summary>
public interface ISuperAdminDataService
{
    /// <summary>
    /// Returns batches from every organisation's database, tagged with
    /// <see cref="BatchSummary.OrganizationId"/> so callers can correlate
    /// them back to the org registry.
    /// </summary>
    Task<BatchDetail[]> GetAllBatchesAcrossOrgsAsync();
}
