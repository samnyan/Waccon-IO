namespace OverlayWebview;

internal static class DiagnosticLog
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "OverlayWebview.log");
    private static volatile bool _enabled;

    internal static void Configure(bool enabled)
    {
        _enabled = enabled || Environment.GetEnvironmentVariable("OVERLAYWEBVIEW_FORCE_LOGGING") == "1";
    }

    internal static void Write(string message)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(Path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
