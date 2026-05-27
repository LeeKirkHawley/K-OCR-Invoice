using System.Security.Claims;
using System.Text;
using K_OCR.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace KOCRAsp.Tests.Helpers;

public static class ControllerTestHelper
{
    /// <summary>
    /// Creates a ControllerContext with a ClaimsPrincipal containing the standard
    /// claims used by KOCRAsp controllers.
    /// </summary>
    public static ControllerContext CreateControllerContext(
        string userId = "user-id-123",
        string orgId = "org-id-123",
        string orgName = "TestOrg",
        string role = "OrganizationUser",
        ISession? session = null)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(AppClaimTypes.OrganizationId, orgId),
            new Claim(AppClaimTypes.TenantName, orgName),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var user = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = user };
        httpContext.Request.Path = "/";
        if (session != null)
            httpContext.Session = session;

        return new ControllerContext { HttpContext = httpContext };
    }

    /// <summary>
    /// Creates a mock ISession. If <paramref name="currentBatchId"/> is provided,
    /// GetString("CurrentBatchId") returns that value; otherwise returns null.
    /// </summary>
    public static Mock<ISession> CreateMockSession(string? currentBatchId = null)
    {
        var mockSession = new Mock<ISession>();

        if (currentBatchId != null)
        {
            var bytes = Encoding.UTF8.GetBytes(currentBatchId);
            mockSession
                .Setup(s => s.TryGetValue("CurrentBatchId", out bytes))
                .Returns(true);
        }
        else
        {
            byte[]? nullBytes = null;
            mockSession
                .Setup(s => s.TryGetValue(It.IsAny<string>(), out nullBytes))
                .Returns(false);
        }

        mockSession.Setup(s => s.Set(It.IsAny<string>(), It.IsAny<byte[]>()));
        return mockSession;
    }

    /// <summary>
    /// Extracts the JSON value from a JsonResult and deserializes it as a
    /// dynamic object for simple assertion.
    /// </summary>
    public static dynamic? GetJsonValue(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        return json.Value;
    }
}
