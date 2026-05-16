namespace K_OCR.Services;

public interface IStripeProvisioningService
{
    /// <summary>Creates a Stripe Customer for the given org and returns the Stripe customer ID.</summary>
    Task<string> CreateCustomerAsync(string orgId, string orgName);

    /// <summary>Creates a Stripe Subscription for the customer and returns the result.</summary>
    Task<StripeSubscriptionResult> CreateSubscriptionAsync(string stripeCustomerId, string priceId);
}

public record StripeSubscriptionResult(
    string SubscriptionId,
    string SubscriptionItemId,
    string Status);
