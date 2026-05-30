using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using KOCRAsp.Controllers;
using K_OCR.Identity;
using KOCRAsp.Models;
using K_OCR.Services;
using KOCRAsp.Services;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly Mock<IAuthService> _mockAuthSvc;
    private readonly Mock<IEmailService> _mockEmailSvc;
    private readonly Mock<ISuperAdminService> _mockSuperAdminSvc;
    private readonly Mock<ILogger<AuthController>> _mockLogger;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

        _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            _mockUserManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);

        _mockAuthSvc = new Mock<IAuthService>();
        _mockEmailSvc = new Mock<IEmailService>();
        _mockSuperAdminSvc = new Mock<ISuperAdminService>();
        _mockLogger = new Mock<ILogger<AuthController>>();

        _controller = new AuthController(
            _mockSignInManager.Object,
            _mockUserManager.Object,
            _mockAuthSvc.Object,
            _mockEmailSvc.Object,
            _mockSuperAdminSvc.Object,
            _mockLogger.Object);

        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext();
        _controller.TempData = new TempDataDictionary(
            _controller.ControllerContext.HttpContext,
            Mock.Of<ITempDataProvider>());
    }

    [Fact]
    public void Login_GET_ReturnsView()
    {
        // The user is not authenticated (default ClaimsPrincipal has no auth type)
        var unauthContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
        };
        _controller.ControllerContext = unauthContext;

        var result = _controller.Login();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<LoginViewModel>(viewResult.Model);
    }

    [Fact]
    public async Task Login_POST_ValidCredentials_RedirectsToHome()
    {
        var model = new LoginViewModel { Email = "user@test.com", Password = "Pass123!" };

        _mockSignInManager
            .Setup(s => s.PasswordSignInAsync(model.Email, model.Password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var result = await _controller.Login(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    [Fact]
    public async Task Login_POST_InvalidCredentials_ReturnsViewWithError()
    {
        var model = new LoginViewModel { Email = "user@test.com", Password = "WrongPass" };

        _mockSignInManager
            .Setup(s => s.PasswordSignInAsync(model.Email, model.Password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        var result = await _controller.Login(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.True(_controller.ModelState.ContainsKey(string.Empty));
    }

    [Fact]
    public async Task Logout_POST_SignsOutAndRedirectsToLogin()
    {
        _mockSignInManager
            .Setup(s => s.SignOutAsync())
            .Returns(Task.CompletedTask);

        var result = await _controller.Logout();

        _mockSignInManager.Verify(s => s.SignOutAsync(), Times.Once);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AuthController.Login), redirect.ActionName);
    }

    [Fact]
    public void ChangePassword_GET_ReturnsView()
    {
        var result = _controller.ChangePassword();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<ChangePasswordViewModel>(viewResult.Model);
    }

    [Fact]
    public async Task ChangePassword_POST_Success_SignsOutAndRedirectsToLogin()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "user@test.com" };
        var model = new ChangePasswordViewModel
        {
            CurrentPassword = "OldPass123!",
            NewPassword = "NewPass123!",
            ConfirmPassword = "NewPass123!"
        };

        _mockUserManager
            .Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);
        _mockUserManager
            .Setup(m => m.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword))
            .ReturnsAsync(IdentityResult.Success);
        _mockSignInManager
            .Setup(m => m.SignOutAsync())
            .Returns(Task.CompletedTask);

        var result = await _controller.ChangePassword(model);

        _mockSignInManager.Verify(m => m.SignOutAsync(), Times.Once);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AuthController.Login), redirect.ActionName);
    }

    [Fact]
    public async Task ChangePassword_POST_Failure_ReturnsViewWithErrors()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "user@test.com" };
        var model = new ChangePasswordViewModel
        {
            CurrentPassword = "WrongPass123!",
            NewPassword = "NewPass123!",
            ConfirmPassword = "NewPass123!"
        };

        _mockUserManager
            .Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);
        _mockUserManager
            .Setup(m => m.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Current password is incorrect." }));

        var result = await _controller.ChangePassword(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(model, viewResult.Model);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains(_controller.ModelState[string.Empty]!.Errors, error => error.ErrorMessage == "Current password is incorrect.");
    }

    [Theory]
    [InlineData("user@test.com")]
    [InlineData("Guest1")]
    [InlineData("guest27")]
    public void LoginViewModel_AllowsEmailOrGuestUserName(string identifier)
    {
        var model = new LoginViewModel { Email = identifier, Password = "Pass123!" };

        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData("Guest")]
    [InlineData("GuestABC")]
    [InlineData("not-an-email")]
    public void LoginViewModel_RejectsNonGuestNonEmailIdentifiers(string identifier)
    {
        var model = new LoginViewModel { Email = identifier, Password = "Pass123!" };

        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result => result.MemberNames.Contains(nameof(LoginViewModel.Email)));
    }
}
