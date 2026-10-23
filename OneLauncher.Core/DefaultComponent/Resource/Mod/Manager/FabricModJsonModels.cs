using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Mod.ModManager;

[JsonSerializable(typeof(FabricModJson))]
public partial class FabricModJsonContext : JsonSerializerContext { }

/// <summary>
/// fabric.mod.json 的顶层结构。
/// </summary>
public class FabricModJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}
