using K_OCRLib.Models;

namespace K_OCRLib.Services.Interfaces;

/// <summary>
/// Provides cross-org data access for super-admin operations by enumerating
/// each organisation's per-org SQLite database.  Do NOT inject this into
/// regular org-user code paths — use <see cref="IBatchService"/> instead.
/// </summary>
public interface ISuperAdminDataService
{
    /// <summary>
    /// Returns batches from every organisation's database, tagged with
    /// <see cref="BatchDetail.OrganizationId"/> so callers can correlate
    /// them back to the org registry.
    /// </summary>
    Task<BatchDetail[]> GetAllBatchesAcrossOrgsAsync();

    /// <summary>
    /// Returns all users in the system with their active organization context.
    /// </summary>
    Task<SuperAdminUserDetail[]> GetAllUsersAsync();
}
