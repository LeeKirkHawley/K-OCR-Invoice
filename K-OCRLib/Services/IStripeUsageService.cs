namespace K_OCR.Services;

public interface IStripeUsageService
{
    /// <summary>Reports OCR page usage to Stripe for a customer via a Billing Meter event.</summary>
    Task ReportUsageAsync(string stripeCustomerId, long pageCount, string? idempotencyKey = null);

    /// <summary>Returns true if the subscription status allows OCR to proceed.</summary>
    bool IsStatusActive(string subscriptionStatus);
}
