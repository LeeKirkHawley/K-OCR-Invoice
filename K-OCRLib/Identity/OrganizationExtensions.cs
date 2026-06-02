namespace K_OCRLib.Identity;

public static class OrganizationExtensions
{
    public static bool IsTrialOrganization(this Organization organization) =>
        organization.IsGuestOrganization || organization.IsBetaTestOrganization;
}
