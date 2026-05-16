using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe.Billing;

namespace K_OCR.Services;

public class StripeUsageService : IStripeUsageService
{
    private readonly string _meterEventName;
    private readonly ILogger<StripeUsageService> _logger;

    public StripeUsageService(IConfiguration configuration, ILogger<StripeUsageService> logger)
    {
        _logger = logger;
        Stripe.StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey is not configured.");
        _meterEventName = configuration["Stripe:MeterEventName"]
            ?? throw new InvalidOperationException("Stripe:MeterEventName is not configured.");
    }

    public async Task ReportUsageAsync(string stripeCustomerId, long pageCount)
    {
        try
        {
            var service = new MeterEventService();
            // Apparently, CreateAsync actually sends the event to Stripe.
            await service.CreateAsync(new MeterEventCreateOptions
            {
                EventName = _meterEventName,
                Payload   = new Dictionary<string, string>
                {
                    ["stripe_customer_id"] = stripeCustomerId,
                    ["value"]              = pageCount.ToString(),
                },
            });

            _logger.LogInformation("Reported {PageCount} page(s) to Stripe meter for customer {CustomerId}.",
                pageCount, stripeCustomerId);
        }
        catch (Exception ex)
        {
            // Stripe failure must never abort OCR — log and swallow.
            _logger.LogError(ex, "Failed to report {PageCount} page(s) to Stripe for customer {CustomerId}.",
                pageCount, stripeCustomerId);
        }
    }

    public bool IsStatusActive(string subscriptionStatus) =>
        subscriptionStatus is "active" or "trialing";
}
