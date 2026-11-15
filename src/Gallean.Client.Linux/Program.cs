using Avalonia;

namespace Gallean.Client.Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ClientApp
            .Configure(useDirectRendering: true)
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                // Software keeps the game starting (slowly) when no usable OpenGL driver is available, e.g. a broken
                // NVIDIA install, VNC or a VM, instead of exiting with "no options were applied".
                RenderingMode =
                [
                    X11RenderingMode.Glx,
                    X11RenderingMode.Egl,
                    X11RenderingMode.Software
                ],
                OverlayPopups = true
            })
            .StartWithClassicDesktopLifetime(args);
    }
}
