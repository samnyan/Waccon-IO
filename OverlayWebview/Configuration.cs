using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverlayWebview;

internal sealed record OverlayConfig
{
    public TargetConfig Target { get; init; } = new();
    public WebConfig Web { get; init; } = new();
    public OverlayMargins Overlay { get; init; } = new();
    public LoggingConfig Logging { get; init; } = new();
}

internal sealed record TargetConfig
{
    public string ProcessName { get; init; } = "Mercury-Win64-Shipping";
    public string TitlePattern { get; init; } = "Mercury";
    public string MatchMode { get; init; } = "contains";
}

internal sealed record WebConfig
{
    public string Url { get; init; } = "http://127.0.0.1:52469/web/";
}

internal sealed record OverlayMargins
{
    public int Left { get; init; } = 320;
    public int Top { get; init; } = 180;
    public int Right { get; init; } = 320;
    public int Bottom { get; init; } = 180;
    public int OffsetX { get; init; }
    public int OffsetY { get; init; } = -42;
    public bool Topmost { get; init; } = true;
}

internal sealed record LoggingConfig
{
    public bool Enabled { get; init; }
}

internal sealed record SettingsMessage
{
    public string Type { get; init; } = string.Empty;
    public OverlayConfig? Config { get; init; }
}

internal sealed record SettingsResponse(string Type, OverlayConfig? Config = null, IReadOnlyList<WindowCandidate>? Windows = null, string? Message = null);

internal sealed record WindowCandidate(long Handle, int ProcessId, string ProcessName, string Title);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(OverlayConfig))]
[JsonSerializable(typeof(SettingsMessage))]
[JsonSerializable(typeof(SettingsResponse))]
[JsonSerializable(typeof(WindowCandidate))]
[JsonSerializable(typeof(List<WindowCandidate>))]
[JsonSerializable(typeof(OverlayLayoutMessage))]
internal sealed partial class OverlayJsonContext : JsonSerializerContext;

internal sealed class ConfigStore(string path)
{
    private readonly string _path = path;

    public OverlayConfig Load()
    {
        if (!File.Exists(_path))
        {
            var initial = new OverlayConfig();
            Save(initial);
            return initial;
        }

        using var stream = File.OpenRead(_path);
        return JsonSerializer.Deserialize(stream, OverlayJsonContext.Default.OverlayConfig) ?? new OverlayConfig();
    }

    public void Save(OverlayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, config, OverlayJsonContext.Default.OverlayConfig);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

internal static class OverlayConfigNormalizer
{
    private const int MaximumMargin = 10_000;
    private const int MaximumOffset = 10_000;

    internal static OverlayConfig Normalize(OverlayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var target = config.Target ?? new TargetConfig();
        var web = config.Web ?? new WebConfig();
        var overlay = config.Overlay ?? new OverlayMargins();
        var logging = config.Logging ?? new LoggingConfig();
        var urlText = string.IsNullOrWhiteSpace(web.Url) ? new WebConfig().Url : web.Url;
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Web URL must be an absolute http or https URL.");
        }

        return new OverlayConfig
        {
            Target = target with
            {
                ProcessName = target.ProcessName?.Trim() ?? string.Empty,
                TitlePattern = target.TitlePattern?.Trim() ?? string.Empty,
                MatchMode = string.Equals(target.MatchMode, "exact", StringComparison.OrdinalIgnoreCase) ? "exact" : "contains",
            },
            Web = web with { Url = url.AbsoluteUri },
            Overlay = overlay with
            {
                Left = Math.Clamp(overlay.Left, 0, MaximumMargin),
                Top = Math.Clamp(overlay.Top, 0, MaximumMargin),
                Right = Math.Clamp(overlay.Right, 0, MaximumMargin),
                Bottom = Math.Clamp(overlay.Bottom, 0, MaximumMargin),
                OffsetX = Math.Clamp(overlay.OffsetX, -MaximumOffset, MaximumOffset),
                OffsetY = Math.Clamp(overlay.OffsetY, -MaximumOffset, MaximumOffset),
            },
            Logging = logging,
        };
    }
}
