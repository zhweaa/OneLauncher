using System.Text.Json.Serialization;

namespace OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;
[JsonSerializable(typeof(ModrinthSearch))]
[JsonSerializable(typeof(ModrinthProjectHit))]
public partial class ModrinthSearchJsonContext : JsonSerializerContext { }
public class ModrinthSearch
{
    [JsonPropertyName("hits")]
    public List<ModrinthProjectHit> Hits { get; set; }
}
public class ModrinthProjectHit
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; }

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; set; }

    [JsonPropertyName("date_created")]
    public DateTime DateCreated { get; set; }
    [JsonPropertyName("display_categories")]
    public List<string> Categories { get; set; }
}
