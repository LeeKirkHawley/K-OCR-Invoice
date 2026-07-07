namespace K_OCRLib.Services.Interfaces;

public interface IStripeProvisioningService
{
    /// <summary>Creates a Stripe Customer for the given org and returns the Stripe customer ID.</summary>
    Task<string> CreateCustomerAsync(string orgId, string orgName, string adminEmail);

    /// <summary>Creates a Stripe Subscription for the customer and returns the result.</summary>
    Task<StripeSubscriptionResult> CreateSubscriptionAsync(string stripeCustomerId, string priceId);

    /// <summary>Fetches the current status of an existing subscription directly from Stripe.</summary>
    Task<string> GetSubscriptionStatusAsync(string stripeSubscriptionId);
}

public record StripeSubscriptionResult(
    string SubscriptionId,
    string SubscriptionItemId,
    string Status);
