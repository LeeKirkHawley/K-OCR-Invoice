using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using KOCRAsp.Data;

namespace KOCRAsp.Controllers;

[ApiController]
[Route("api/webhook/stripe")]
[IgnoreAntiforgeryToken]
public class StripeWebhookController : ControllerBase
{
    private readonly string _webhookSecret;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<StripeWebhookController> logger)
    {
        _webhookSecret = configuration["Stripe:WebhookSecret"]
            ?? throw new ArgumentNullException("Stripe:WebhookSecret is missing in configuration");
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook()
    {
        string json;
        using (var reader = new StreamReader(Request.Body))
            json = await reader.ReadToEndAsync();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                _webhookSecret,
                throwOnApiVersionMismatch: false,
                tolerance: 300);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature verification failed: {Message}", ex.Message);
            return BadRequest("Invalid signature");
        }

        _logger.LogInformation("Received Stripe event: {EventType} | ID: {EventId}", stripeEvent.Type, stripeEvent.Id);

        // Return 200 immediately so Stripe doesn't retry; process in background
        _ = Task.Run(async () => await ProcessEventAsync(stripeEvent));

        return Ok();
    }

    private async Task ProcessEventAsync(Event stripeEvent)
    {
        try
        {
            switch (stripeEvent.Type)
            {
                case EventTypes.CustomerSubscriptionUpdated:
                    var updated = stripeEvent.Data.Object as Subscription;
                    await UpdateSubscriptionStatusAsync(updated!.CustomerId, updated.Status);
                    break;

                case EventTypes.CustomerSubscriptionDeleted:
                    var deleted = stripeEvent.Data.Object as Subscription;
                    await UpdateSubscriptionStatusAsync(deleted!.CustomerId, "canceled");
                    break;

                case EventTypes.InvoicePaymentSucceeded:
                    var paid = stripeEvent.Data.Object as Invoice;
                    await UpdateSubscriptionStatusAsync(paid!.CustomerId, "active");
                    break;

                case EventTypes.InvoicePaymentFailed:
                    var failed = stripeEvent.Data.Object as Invoice;
                    await UpdateSubscriptionStatusAsync(failed!.CustomerId, "past_due");
                    break;

                default:
                    _logger.LogInformation("Unhandled Stripe event: {Type}", stripeEvent.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe event {EventId} of type {Type}",
                stripeEvent.Id, stripeEvent.Type);
        }
    }

    private async Task UpdateSubscriptionStatusAsync(string stripeCustomerId, string status)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var org = await db.Organizations
            .FirstOrDefaultAsync(o => o.StripeCustomerId == stripeCustomerId);

        if (org is null)
        {
            _logger.LogWarning("No organization found for Stripe customer {CustomerId}", stripeCustomerId);
            return;
        }

        org.StripeSubscriptionStatus = status;
        await db.SaveChangesAsync();

        _logger.LogInformation("Updated org {OrgId} subscription status to {Status}", org.Id, status);
    }
}