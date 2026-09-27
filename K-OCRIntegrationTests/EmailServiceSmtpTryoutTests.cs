using K_OCRLib.Configuration;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRIntegrationTests;

/// <summary>
/// Scratchpad for trial-and-error'ing real SMTP server settings against
/// EmailService.SendAsync (invoked indirectly via the public SendTestEmailAsync).
///
/// Edit the constants below with the server/credentials you want to try, then run:
///   dotnet test K-OCRIntegrationTests --filter SendTestEmailAsync_TriesCandidateSmtpSettings
///
/// SmtpHost is blank by default so this test self-skips in normal runs / CI.
/// The SMTP protocol log (full client/server conversation) is written to the console
/// via a Debug-level console logger, so auth/TLS failures are easy to diagnose.
/// </summary>
public class EmailServiceSmtpTryoutTests
{
    // ── Edit these to try a different server / account ──────────────────────
    private const string SmtpHost = "elkmountainsoftware.com";                 // e.g. "smtp.gmail.com", "smtp.office365.com"
    private const int Port = 587;                       // 465 = SslOnConnect, 587/25 = StartTls (via EnableSsl+Auto)
    private const bool EnableSsl = true;
    private const bool SkipCertificateValidation = true;
    private const string? Username = "admin@elkmountainsoftware.com";               // e.g. "someone @example.com"
    private const string? Password = "EMSRudeboy10";
    private const string? FromAddress = "admin@elkmountainsoftware.com";             // falls back to Username if blank
    private const string FromName = "Elk Mountain Software";

    //works
    //private const string ToEmail = "leekirkhawley@gmail.com";

    //private const string ToEmail = "admin@elkmountainsoftware.com";
    private const string ToEmail = "fredboggs@hardscrabble.org";

    [Fact]
    public async Task SendTestEmailAsync_TriesCandidateSmtpSettings()
    {
        if (string.IsNullOrWhiteSpace(SmtpHost))
        {
            Console.WriteLine("Skipping: set SmtpHost (and Username/Password as needed) at the top of this file to try a server.");
            return;
        }

        var emailSettings = new EmailSettings
        {
            SmtpHost = SmtpHost,
            Port = Port,
            EnableSsl = EnableSsl,
            SkipCertificateValidation = SkipCertificateValidation,
            Username = Username,
            Password = Password,
            FromAddress = FromAddress,
            FromName = FromName
        };

        var configService = new Mock<IConfigurationService>();
        configService
            .Setup(c => c.LoadSettingsAsync(It.IsAny<string?>()))
            .ReturnsAsync(new AppSettings { Email = emailSettings });

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<EmailService>();

        var emailService = new EmailService(configService.Object, logger);

        // EmailService.SendAsync is private; SendTestEmailAsync is the thinnest public
        // wrapper that builds a message and calls straight into it.
        await emailService.SendTestEmailAsync(ToEmail);

        Console.WriteLine($"Sent OK via {SmtpHost}:{Port} (EnableSsl={EnableSsl}) as {Username ?? "(anonymous)"}.");
    }
}
