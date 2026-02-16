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

        // SESSION_MANAGER guard: clear it to prevent ICE errors
        // The X11 session manager can cause "ICE default IO error handler" crashes
        // when the socket is stale or unreachable. Since Avalonia doesn't require it,
        // we clear it preventively.
        Environment.SetEnvironmentVariable("SESSION_MANAGER", "");

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
