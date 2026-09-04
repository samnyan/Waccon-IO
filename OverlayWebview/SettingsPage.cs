using System.Reflection;

namespace OverlayWebview;

internal static class SettingsPage
{
    private static readonly Lazy<string> Content = new(Load);

    public static string Html => Content.Value;

    private static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith("Web.settings.html", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException("Embedded settings page is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
