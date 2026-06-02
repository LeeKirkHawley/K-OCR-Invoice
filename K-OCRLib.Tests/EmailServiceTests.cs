using System.Net;
using System.Net.Sockets;
using System.Text;
using K_OCRLib.Configuration;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace K_OCRLib.Tests;

public class EmailServiceTests
{
    [Fact]
    public async Task SendOrgAdminInviteAsync_SendsExpectedMessage()
    {
        await using var server = new FakeSmtpServer();
        await server.StartAsync();

        var config = new Mock<IConfigurationService>();
        config.Setup(s => s.LoadSettingsAsync(It.IsAny<string?>()))
            .ReturnsAsync(new AppSettings
            {
                Email = new EmailSettings
                {
                    SmtpHost = "127.0.0.1",
                    Port = server.Port,
                    EnableSsl = false,
                    Username = null,
                    FromAddress = "noreply@test.com",
                    FromName = "K-OCR"
                }
            });

        var service = new EmailService(config.Object, Mock.Of<ILogger<EmailService>>());
        var setupLink = "https://example.com/auth/setpassword?userId=1&token=abc";

        await service.SendOrgAdminInviteAsync("admin@test.com", "Admin User", "Acme Org", setupLink);

        var message = await server.Message.Task;
        Assert.Contains("Subject: Your Acme Org account on K-OCR is ready", message);
        Assert.Contains("admin@test.com", message);
        Assert.Contains(setupLink, message);
    }

    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private CancellationTokenSource? _cts;
        public int Port { get; private set; }
        public TaskCompletionSource<string> Message { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(ct);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

                await writer.WriteLineAsync("220 localhost ESMTP");
                var message = new StringBuilder();
                bool inData = false;

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null)
                        break;

                    if (inData)
                    {
                        if (line == ".")
                        {
                            await writer.WriteLineAsync("250 queued");
                            Message.TrySetResult(message.ToString());
                            inData = false;
                        }
                        else
                        {
                            message.AppendLine(line);
                        }
                        continue;
                    }

                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250-localhost");
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase) ||
                             line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        inData = true;
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 Bye");
                        break;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();
            _listener.Stop();
            await Task.CompletedTask;
        }
    }
}
