using K_OCRLib.Models;

namespace K_OCRLib.Services.Interfaces;

/// <summary>Service to track trial organization limits (beta and guest).</summary>
public interface ITrialOrganizationLimitService
{
    /// <summary>Get the current limit status for the tenant organization.</summary>
    Task<TrialOrganizationLimitStatus> GetCurrentStatusAsync();
}
