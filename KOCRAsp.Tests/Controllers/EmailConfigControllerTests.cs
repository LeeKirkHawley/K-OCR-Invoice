using K_OCR.Configuration;
using K_OCR.Services;
using KOCRAsp.Controllers;
using KOCRAsp.Services;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class EmailConfigControllerTests
{
    private readonly Mock<IConfigurationService> _mockConfigSvc;
    private readonly Mock<IEmailService> _mockEmailSvc;
    private readonly Mock<ILogger<EmailConfigController>> _mockLogger;
    private readonly EmailConfigController _controller;

    public EmailConfigControllerTests()
    {
        _mockConfigSvc = new Mock<IConfigurationService>();
        _mockEmailSvc = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<EmailConfigController>>();

        _controller = new EmailConfigController(
            _mockConfigSvc.Object,
            _mockEmailSvc.Object,
            _mockLogger.Object);

        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext(role: "OrganizationAdmin");
    }

    [Fact]
    public async Task Index_ReturnsView()
    {
        var settings = new AppSettings { Email = new EmailSettings { SmtpHost = "smtp.example.com" } };
        _mockConfigSvc
            .Setup(s => s.LoadSettingsAsync(null))
            .ReturnsAsync(settings);

        var result = await _controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<EmailSettings>(viewResult.Model);
        Assert.Equal("smtp.example.com", model.SmtpHost);
    }

    [Fact]
    public async Task SendTest_POST_ValidEmail_ReturnsSuccess()
    {
        _mockEmailSvc
            .Setup(s => s.SendTestEmailAsync("admin@example.com"))
            .Returns(Task.CompletedTask);

        var result = await _controller.SendTest("admin@example.com");

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        var successProp = value.GetType().GetProperty("success");
        Assert.NotNull(successProp);
        Assert.True((bool)successProp.GetValue(value)!);

        _mockEmailSvc.Verify(s => s.SendTestEmailAsync("admin@example.com"), Times.Once);
    }

    [Fact]
    public async Task SendTest_POST_ServiceThrows_ReturnsError()
    {
        _mockEmailSvc
            .Setup(s => s.SendTestEmailAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP not configured"));

        var result = await _controller.SendTest("admin@example.com");

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        var successProp = value.GetType().GetProperty("success");
        Assert.NotNull(successProp);
        Assert.False((bool)successProp.GetValue(value)!);

        var errorProp = value.GetType().GetProperty("error");
        Assert.NotNull(errorProp);
        Assert.Equal("SMTP not configured", (string)errorProp.GetValue(value)!);
    }
}
