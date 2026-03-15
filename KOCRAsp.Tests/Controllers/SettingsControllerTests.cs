using K_OCR.Configuration;
using K_OCR.Services;
using KOCRAsp.Controllers;
using KOCRAsp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;

namespace KOCRAsp.Tests.Controllers;

public class SettingsControllerTests
{
    private readonly Mock<IConfigurationService> _mockConfigSvc;
    private readonly Mock<ILogger<SettingsController>> _mockLogger;
    private readonly SettingsController _controller;

    public SettingsControllerTests()
    {
        _mockConfigSvc = new Mock<IConfigurationService>();
        _mockLogger = new Mock<ILogger<SettingsController>>();
        _controller = new SettingsController(_mockConfigSvc.Object, _mockLogger.Object);
        _controller.ControllerContext = ControllerTestHelper.CreateControllerContext();
        _controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
    }

    [Fact]
    public async Task Index_LoadsSettingsAndReturnsView()
    {
        var settings = new AppSettings
        {
            OCRProvider = "Azure"
        };
        _mockConfigSvc
            .Setup(s => s.LoadSettingsAsync(null))
            .ReturnsAsync(settings);

        var result = await _controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AppSettings>(viewResult.Model);
        Assert.Equal("Azure", model.OCRProvider);
    }

    [Fact]
    public async Task Save_POST_ValidSettings_RedirectsToIndex()
    {
        var settings = new AppSettings { OCRProvider = "Tesseract" };
        _mockConfigSvc
            .Setup(s => s.SaveSettingsAsync(settings, null))
            .Returns(Task.CompletedTask);

        var result = await _controller.Save(settings);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SettingsController.Index), redirect.ActionName);
    }
}
