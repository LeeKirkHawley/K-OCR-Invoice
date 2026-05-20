using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

public class StripeProvisioningService : IStripeProvisioningService
{
    private readonly ILogger<StripeProvisioningService> _logger;

    public StripeProvisioningService(IConfiguration configuration, ILogger<StripeProvisioningService> logger)
    {
        _logger = logger;
        Stripe.StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey is not configured.");
    }

    public async Task<string> CreateCustomerAsync(string orgId, string orgName)
    {
        var service = new Stripe.CustomerService();
        var customer = await service.CreateAsync(new Stripe.CustomerCreateOptions
        {
            Name = orgName,
            Metadata = new Dictionary<string, string> { ["OrgId"] = orgId },
        });
        _logger.LogInformation("Created Stripe customer {CustomerId} for org {OrgId}.", customer.Id, orgId);
        return customer.Id;
    }

    public async Task<StripeSubscriptionResult> CreateSubscriptionAsync(string stripeCustomerId, string priceId)
    {
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
        var service = new Stripe.SubscriptionService();
        var subscription = await service.GetAsync(stripeSubscriptionId);
        _logger.LogInformation(
            "Fetched Stripe subscription {SubId}: status={Status}.",
            stripeSubscriptionId, subscription.Status);
        return subscription.Status;
    }
}
