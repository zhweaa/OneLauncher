using System.Text.Json.Serialization;

namespace OneLauncher.Core.DefaultComponent.Resource.Pack
{

    [JsonSerializable(typeof(ModrinthManifest))]
    [JsonSerializable(typeof(FinalVersionInfo))]
    public partial class MrpackJsonContext : JsonSerializerContext
    {
    }

    public class ModrinthManifest
    {
        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; set; }

        [JsonPropertyName("game")]
        public string Game { get; set; }

        [JsonPropertyName("versionId")]
        public string VersionId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("dependencies")]
        public Dictionary<string, string> Dependencies { get; set; }

        [JsonPropertyName("files")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ModrinthFile>? Files { get; set; }
    }

    public class ModrinthFile
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("hashes")]
        public Dictionary<string, string> Hashes { get; set; }

        [JsonPropertyName("downloads")]
        public List<string> Downloads { get; set; }

        [JsonPropertyName("fileSize")]
        public int FileSize { get; set; }
    }
    public class FinalVersionInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("inheritsFrom")]
        public string InheritsFrom { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "release"; 
    }
}
