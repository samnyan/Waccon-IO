using DirectN.Extensions.Utilities;

namespace OverlayWebview;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00000000", EnvironmentVariableTarget.Process);
        using var application = new Application();
        using var host = new OverlayHost();
        host.Start();
        application.Run();
    }
}
