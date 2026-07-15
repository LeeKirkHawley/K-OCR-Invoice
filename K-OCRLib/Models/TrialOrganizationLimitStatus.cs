using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCRLib.Models
{
    public sealed record TrialOrganizationLimitStatus(
        bool IsBetaTestOrganization,
        bool IsGuestOrganization,
        int MaxOcrPages,
        int UsedOcrPages,
        int MaxBatches,
        int UsedBatches)
    {
        public int RemainingOcrPages => Math.Max(0, MaxOcrPages - UsedOcrPages);
        public bool IsOcrLimitExceeded()
        {
            if (!IsBetaTestOrganization && !IsGuestOrganization)
                return false;
            else
                return UsedOcrPages >= MaxOcrPages;
        }

        public int RemainingBatches => Math.Max(0, MaxBatches - UsedBatches);
        public bool IsBatchLimitExceeded()
        {
            if (!IsBetaTestOrganization && !IsGuestOrganization)
                return false;
            else
                return UsedBatches >= MaxBatches;
        }

        public bool IsTrialOrganization => IsBetaTestOrganization || IsGuestOrganization;
    }

}
