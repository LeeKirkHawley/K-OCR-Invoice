using Avalonia;
using System;
using System.IO;

namespace K_OCRDesktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Let Avalonia pick the best platform backend automatically (do not force GTK3 here).
        // To force a backend for troubleshooting, set the AVALONIA_BACKEND environment variable externally.

        // SESSION_MANAGER guard: clear if it references a missing /tmp/.ICE-unix socket
        // (prevents native libICE/libSM exit when the session manager socket is stale)
        var sessionManager = Environment.GetEnvironmentVariable("SESSION_MANAGER");
        if (!string.IsNullOrEmpty(sessionManager))
        {
            const string marker = "/tmp/.ICE-unix/";
            var idx = sessionManager.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var start = idx + marker.Length;
                var idBuilder = new System.Text.StringBuilder();
                for (var i = start; i < sessionManager.Length; i++)
                {
                    var ch = sessionManager[i];
                    if (char.IsDigit(ch)) idBuilder.Append(ch);
                    else break;
                }

                if (idBuilder.Length > 0)
                {
                    var sockPath = marker + idBuilder.ToString();
                    if (!File.Exists(sockPath))
                    {
                        // clear silently so native libICE won't call exit()
                        Environment.SetEnvironmentVariable("SESSION_MANAGER", "");
                    }
                }
            }
        }

        // WAYLAND_DISPLAY guard: if WAYLAND_DISPLAY is set but the runtime socket is missing,
        // clear it so Avalonia/GTK won't select a broken Wayland backend.
        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(wayland) && !string.IsNullOrEmpty(xdg))
        {
            var waylandSock = Path.Combine(xdg, wayland);
            if (!File.Exists(waylandSock))
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "");
            }
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
