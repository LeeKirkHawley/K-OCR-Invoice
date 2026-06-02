namespace K_OCRLib.Models.Api.SuperAdmin;

public class ProvisionStripeRequest
{
    public string OrgId { get; set; } = string.Empty;
    /// <summary>Stripe Price ID to subscribe the org to. If empty, falls back to Stripe:DefaultPriceId config value.</summary>
    public string? PriceId { get; set; }
}
