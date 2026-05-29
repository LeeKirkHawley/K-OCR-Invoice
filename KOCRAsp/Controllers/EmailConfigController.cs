using K_OCR.Configuration;
using K_OCR.Services;
using KOCRAsp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOCRAsp.Controllers;

[Authorize(Policy = "OrgAdminOrSuperAdmin")]
public class EmailConfigController : Controller
{
    private readonly IConfigurationService _configSvc;
    private readonly IEmailService _emailSvc;
    private readonly ILogger<EmailConfigController> _logger;

    public EmailConfigController(
        IConfigurationService configSvc,
        IEmailService emailSvc,
        ILogger<EmailConfigController> logger)
    {
        _configSvc = configSvc;
        _emailSvc = emailSvc;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var settings = await _configSvc.LoadSettingsAsync();
        return View(settings.Email);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailSettings model)
    {
        try
        {
            var settings = await _configSvc.LoadSettingsAsync();

            // If the password field was left blank, preserve the previously saved password
            // (browsers never pre-fill type="password" inputs, so a blank value means "unchanged").
            if (string.IsNullOrEmpty(model.Password) && settings.Email is not null)
                model.Password = settings.Email.Password;

            settings.Email = model;
            await _configSvc.SaveSettingsAsync(settings);
            TempData["Success"] = "Email settings saved.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveEmailSettings failed");
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest(string toEmail)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return Json(new { success = false, error = "Recipient email is required." });

        try
        {
            await _emailSvc.SendTestEmailAsync(toEmail);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendTest email failed to {ToEmail}", toEmail);
            return Json(new { success = false, error = ex.Message });
        }
    }
}
