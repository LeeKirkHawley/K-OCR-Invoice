using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Services;
using K_OCR.Identity;
using KOCRAsp.Controllers;
using K_OCR.Models.Api.OrganizationAdmin;
using K_OCR.Models.Api.SuperAdmin;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class OrgConfigControllerTests
{
    private readonly Mock<IOrgConfigService> _mockOrgConfigSvc;
    private readonly Mock<ITenantContext> _mockTenantContext;
    private readonly Mock<ISuperAdminService> _mockSuperAdminSvc;
    private readonly Mock<IOrganizationAdminService> _mockOrgAdminSvc;
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<ILogger<OrgConfigController>> _mockLogger;
    private readonly OrgConfigController _controller;

    private const string OrgId   = "org-id-123";
    private const string OrgName = "TestOrg";

    public OrgConfigControllerTests()
    {
        _mockOrgConfigSvc  = new Mock<IOrgConfigService>();
        _mockTenantContext = new Mock<ITenantContext>();
        _mockSuperAdminSvc = new Mock<ISuperAdminService>();
        _mockOrgAdminSvc   = new Mock<IOrganizationAdminService>();
        _mockUserManager   = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        _mockLogger = new Mock<ILogger<OrgConfigController>>();

        _mockTenantContext.Setup(t => t.OrganizationName).Returns(OrgName);
        _mockTenantContext.Setup(t => t.IsSuperAdmin).Returns(false);
        _mockOrgConfigSvc.Setup(s => s.LoadAsync(OrgName))
                         .ReturnsAsync(new OrgConfig { MinConfidenceThreshold = 0.8 });

        _controller = new OrgConfigController(
            _mockOrgConfigSvc.Object,
            _mockTenantContext.Object,
            _mockSuperAdminSvc.Object,
            _mockOrgAdminSvc.Object,
            _mockUserManager.Object,
            _mockLogger.Object);

        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext(
            orgId: OrgId, role: "OrganizationAdmin");
    }

    [Fact]
    public async Task Index_OrgAdmin_ReturnsView()
    {
        var result = await _controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<OrgConfig>(view.Model);
    }

    [Fact]
    public async Task InviteUser_POST_ReturnsSuccess()
    {
        var request = new InviteUserRequest
        {
            Name  = "Jane Doe",
            Email = "jane@example.com",
            Role  = "OrganizationUser"
        };
        var inviteResult = new InviteUserResult
        {
            UserId    = "new-user-id",
            EmailSent = true,
            SetupLink = "https://example.com/set-password?token=abc"
        };

        _mockOrgAdminSvc
            .Setup(s => s.InviteUserAsync(OrgId, request, It.IsAny<string?>()))
            .ReturnsAsync(inviteResult);

        var result = await _controller.InviteUser(request, orgId: null);

        var json      = Assert.IsType<JsonResult>(result);
        var value     = json.Value!;
        var success   = value.GetType().GetProperty("success");
        Assert.NotNull(success);
        Assert.True((bool)success.GetValue(value)!);

        var userIdProp = value.GetType().GetProperty("userId");
        Assert.NotNull(userIdProp);
        Assert.Equal("new-user-id", (string)userIdProp.GetValue(value)!);
    }
}
