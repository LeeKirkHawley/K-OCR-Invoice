using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace K_OCRLib.Services;

public class StripeProvisioningService : IStripeProvisioningService
{
    private readonly string _secretKey;
    private readonly ILogger<StripeProvisioningService> _logger;

    public StripeProvisioningService(IConfiguration configuration, ILogger<StripeProvisioningService> logger)
    {
        _logger = logger;
        _secretKey = configuration["Stripe:SecretKey"] ?? string.Empty;
        if (string.IsNullOrEmpty(_secretKey))
            _logger.LogWarning("Stripe:SecretKey is not configured. Stripe operations will fail if called.");
    }

    private void EnsureApiKey()
    {
        if (string.IsNullOrEmpty(_secretKey))
        {
            _logger.LogError("Stripe:SecretKey is not configured. Cannot perform Stripe operations.");
            throw new InvalidOperationException("Stripe:SecretKey is not configured.");
        }
        Stripe.StripeConfiguration.ApiKey = _secretKey;
    }

    public async Task<string> CreateCustomerAsync(string orgId, string orgName, string adminEmail)
    {
        EnsureApiKey();
        var service = new Stripe.CustomerService();
        var customer = await service.CreateAsync(new Stripe.CustomerCreateOptions
        {
            Name = orgName,
            Email = adminEmail,
            Metadata = new Dictionary<string, string> { ["OrgId"] = orgId },
        });
        _logger.LogInformation("Created Stripe customer {CustomerId} for org {OrgId}.", customer.Id, orgId);
        return customer.Id;
    }

    public async Task<StripeSubscriptionResult> CreateSubscriptionAsync(string stripeCustomerId, string priceId)
    {
        EnsureApiKey();
        var service = new Stripe.SubscriptionService();
        var subscription = await service.CreateAsync(new Stripe.SubscriptionCreateOptions
        {
            Customer = stripeCustomerId,
            Items = new List<Stripe.SubscriptionItemOptions>
            {
                new() { Price = priceId },
            },
        });

        var item = subscription.Items.Data.FirstOrDefault();
        var result = new StripeSubscriptionResult(
            subscription.Id,
            item?.Id ?? string.Empty,
            subscription.Status);

        _logger.LogInformation(
            "Created Stripe subscription {SubId} (status={Status}) for customer {CustomerId}.",
            result.SubscriptionId, result.Status, stripeCustomerId);

        return result;
    }

    public async Task<string> GetSubscriptionStatusAsync(string stripeSubscriptionId)
    {
        EnsureApiKey();
        var service = new Stripe.SubscriptionService();
        var subscription = await service.GetAsync(stripeSubscriptionId);
        _logger.LogInformation(
            "Fetched Stripe subscription {SubId}: status={Status}.",
            stripeSubscriptionId, subscription.Status);
        return subscription.Status;
    }
}
