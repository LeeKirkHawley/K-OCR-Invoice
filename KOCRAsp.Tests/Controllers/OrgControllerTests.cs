using K_OCRLib.Configuration;
using K_OCRLib.Data;
using K_OCRLib.Identity;
using K_OCRLib.Models.Api.OrganizationAdmin;
using K_OCRLib.Security;
using K_OCRLib.Services.Interfaces;
using KOCRAsp.Controllers;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class OrgConfigControllerTests
{
    private readonly Mock<IOrgConfigService> _mockOrgConfigSvc;
    private readonly Mock<ISuperAdminService> _mockSuperAdminSvc;
    private readonly Mock<IOrganizationAdminService> _mockOrgAdminSvc;
    private readonly Mock<IBatchService> _mockBatchSvc;
    private readonly Mock<ILogger<OrgConfigController>> _mockLogger;
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _dbContext;
    private readonly OrgConfigController _controller;

    private const string OrgId   = "org-id-123";
    private const string OrgName = "TestOrg";

    public OrgConfigControllerTests()
    {
        _mockOrgConfigSvc  = new Mock<IOrgConfigService>();
        _mockSuperAdminSvc = new Mock<ISuperAdminService>();
        _mockOrgAdminSvc   = new Mock<IOrganizationAdminService>();
        _mockBatchSvc = new Mock<IBatchService>();
        _mockLogger = new Mock<ILogger<OrgConfigController>>();


        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new ApplicationDbContext(dbOptions);
        _dbContext.Database.EnsureCreated();
        _dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-id-123",
            UserName = "org-admin@test.com",
            Email = "org-admin@test.com",
            FullName = "Org Admin",
            OrganizationId = OrgId
        });
        _dbContext.Organizations.Add(new Organization { Id = OrgId, Name = OrgName, IsActive = true });
        _dbContext.UserOrganizationMemberships.Add(new UserOrganizationMembership
        {
            UserId = "user-id-123",
            OrganizationId = OrgId,
            Role = RoleNames.OrganizationAdmin
        });
        _dbContext.SaveChanges();

        _mockOrgConfigSvc.Setup(s => s.LoadAsync(OrgName))
                         .ReturnsAsync(new OrgConfig { MinConfidenceThreshold = 0.2 });

        _controller = new OrgConfigController(
            _dbContext,
            _mockOrgConfigSvc.Object,
            _mockSuperAdminSvc.Object,
            _mockOrgAdminSvc.Object,
            _mockBatchSvc.Object,
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
