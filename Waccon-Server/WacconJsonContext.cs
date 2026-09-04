using System.Text.Json.Serialization;

namespace Waccon.Server;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ServerOptions.SettingsRoot))]
internal partial class WacconJsonContext : JsonSerializerContext
{
}
