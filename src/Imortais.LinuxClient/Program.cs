using Avalonia;

namespace Imortais.LinuxClient;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Before any capture type is touched: use Ubuntu's libpcap.so.0.8.
        Imortais.LinuxClient.Services.NativeLibraries.Install();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
