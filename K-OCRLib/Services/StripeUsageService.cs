using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe.Billing;

namespace K_OCR.Services;

public class StripeUsageService : IStripeUsageService
{
    private readonly string _secretKey;
    private readonly string _meterEventName;
    private readonly ILogger<StripeUsageService> _logger;

    public StripeUsageService(IConfiguration configuration, ILogger<StripeUsageService> logger)
    {
        _logger = logger;
        _secretKey      = configuration["Stripe:SecretKey"] ?? string.Empty;
        _meterEventName = configuration["Stripe:MeterEventName"] ?? string.Empty;
        if (string.IsNullOrEmpty(_secretKey) || string.IsNullOrEmpty(_meterEventName))
            _logger.LogWarning("Stripe is not fully configured. Usage reporting will be skipped.");
    }

    public async Task ReportUsageAsync(string stripeCustomerId, long pageCount, string? idempotencyKey = null)
    {
        if (string.IsNullOrEmpty(_secretKey) || string.IsNullOrEmpty(_meterEventName))
        {
            _logger.LogDebug("Stripe not configured — skipping usage report for customer {CustomerId}.", stripeCustomerId);
            return;
        }

        Stripe.StripeConfiguration.ApiKey = _secretKey;
        try
        {
            var service = new MeterEventService();
            var options = new MeterEventCreateOptions
            {
                EventName = _meterEventName,
                Payload   = new Dictionary<string, string>
                {
                    ["stripe_customer_id"] = stripeCustomerId,
                    ["value"]              = pageCount.ToString(),
                },
            };
            if (!string.IsNullOrEmpty(idempotencyKey))
                options.Identifier = idempotencyKey;

            await service.CreateAsync(options);

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
