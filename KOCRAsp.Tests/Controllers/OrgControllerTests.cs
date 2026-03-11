using KOCRAsp.Controllers;
using KOCRAsp.Identity;
using KOCRAsp.Models.Api.OrganizationAdmin;
using KOCRAsp.Services;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class OrgControllerTests
{
    private readonly Mock<IOrganizationAdminService> _mockOrgAdminSvc;
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<ILogger<OrgController>> _mockLogger;
    private readonly OrgController _controller;

    private const string OrgId = "org-id-123";

    public OrgControllerTests()
    {
        _mockOrgAdminSvc = new Mock<IOrganizationAdminService>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);
        _mockLogger = new Mock<ILogger<OrgController>>();

        _controller = new OrgController(
            _mockOrgAdminSvc.Object,
            _mockUserManager.Object,
            _mockLogger.Object);

        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext(
            orgId: OrgId, role: "OrganizationAdmin");
    }

    [Fact]
    public void Index_ReturnsView()
    {
        var result = _controller.Index();

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task InviteUser_POST_ReturnsSuccess()
    {
        var request = new InviteUserRequest
        {
            Name = "Jane Doe",
            Email = "jane@example.com",
            Role = "OrganizationUser"
        };
        var inviteResult = new InviteUserResult
        {
            UserId = "new-user-id",
            EmailSent = true,
            SetupLink = "https://example.com/set-password?token=abc"
        };

        _mockOrgAdminSvc
            .Setup(s => s.InviteUserAsync(OrgId, request))
            .ReturnsAsync(inviteResult);

        var result = await _controller.InviteUser(request);

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        var successProp = value.GetType().GetProperty("success");
        Assert.NotNull(successProp);
        Assert.True((bool)successProp.GetValue(value)!);

        var userIdProp = value.GetType().GetProperty("userId");
        Assert.NotNull(userIdProp);
        Assert.Equal("new-user-id", (string)userIdProp.GetValue(value)!);
    }
}
