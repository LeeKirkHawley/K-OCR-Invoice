namespace K_OCR.Identity;

public static class OrganizationExtensions
{
    public static bool IsTrialOrganization(this Organization organization) =>
        organization.IsGuestOrganization || organization.IsBetaTestOrganization;
}
