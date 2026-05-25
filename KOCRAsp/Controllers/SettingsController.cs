using K_OCR.Configuration;
using K_OCR.Security;
using K_OCR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

[Authorize(Roles = RoleNames.SuperAdmin)]
public class SettingsController : Controller
{
    private readonly IConfigurationService _configSvc;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(IConfigurationService configSvc, ILogger<SettingsController> logger)
    {
        _configSvc = configSvc;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var settings = await _configSvc.LoadSettingsAsync();
        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AppSettings model)
    {
        try
        {
            // Email is not part of the Settings form — preserve it fully from the stored file
            // so that saving Settings doesn't wipe the Email config.
            var existing = await _configSvc.LoadSettingsAsync();
            model.Email = existing.Email;

            await _configSvc.SaveSettingsAsync(model);
            TempData["Success"] = "Settings saved successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveSettings failed");
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
