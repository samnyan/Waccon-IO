using System.Text.Json;

namespace Waccon.Server;

/// <summary>Strongly typed server configuration.</summary>
public sealed record ServerOptions
{
    /// <summary>TCP bind address.</summary>
    public string ListenAddress { get; init; } = "127.0.0.1";
    /// <summary>TCP listening port.</summary>
    public int TcpPort { get; init; } = 52468;
    /// <summary>Maximum simultaneous clients.</summary>
    public int MaxClients { get; init; } = 4;
    /// <summary>Input lease timeout in milliseconds.</summary>
    public int LeaseTimeoutMs { get; init; } = 500;
    /// <summary>Maximum accepted payload length.</summary>
    public int MaxPayloadBytes { get; init; } = 4096;
    /// <summary>Optional shared authentication token.</summary>
    public string AuthToken { get; init; } = "";
    /// <summary>Named Windows mapping shared with waccon-io.</summary>
    public string SharedMemoryName { get; init; } = "Local\\WACCON_SHARED_BUFFER";

    /// <summary>Loads and validates appsettings.json.</summary>
    public static ServerOptions Load(string[] args)
    {
        var path = args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ?? "appsettings.json";
        var root = File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), WacconJsonContext.Default.SettingsRoot) ?? new()
            : new();
        var value = root.Server ?? new();
        var shm = root.SharedMemory?.Name ?? "Local\\WACCON_SHARED_BUFFER";
        if (value.TcpPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(TcpPort));
        if (value.MaxClients is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(MaxClients));
        if (value.LeaseTimeoutMs is < 50 or > 60000) throw new ArgumentOutOfRangeException(nameof(LeaseTimeoutMs));
        if (value.MaxPayloadBytes is < 32 or > 1048576) throw new ArgumentOutOfRangeException(nameof(MaxPayloadBytes));
        return value with { SharedMemoryName = shm };
    }


    internal sealed class SettingsRoot { public ServerOptions? Server { get; init; } public SharedMemoryOptions? SharedMemory { get; init; } }
    internal sealed class SharedMemoryOptions { public string? Name { get; init; } }
}
