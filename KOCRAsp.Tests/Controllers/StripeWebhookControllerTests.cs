using KOCRAsp.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Tests.Controllers;

public class StripeWebhookControllerTests
{
    [Fact]
    public void StripeWebhookController_HasPaymentStripeEventsRoute()
    {
        var routes = typeof(StripeWebhookController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Select(a => a.Template)
            .ToArray();

        Assert.Contains("Payment/StripeEvents", routes);
    }
}
