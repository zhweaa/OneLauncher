using System.Text.Json.Serialization;
using static OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.ForgeVersionListGetter;
using System.Text.Json;

[JsonSerializable(typeof(ForgeSeriesVersionJson))]
[JsonSerializable(typeof(ForgeSeriesArguments))]
[JsonSerializable(typeof(ForgeSeriesLibrary))]
[JsonSerializable(typeof(ForgeSeriesDownloads))]
[JsonSerializable(typeof(ForgeSeriesArtifact))]
[JsonSerializable(typeof(ForgeSeriesProcessor))]
[JsonSerializable(typeof(ForgeSeriesInstallProfile))] 
[JsonSerializable(typeof(ForgeLegacyInstall))]
[JsonSerializable(typeof(ForgeSeriesData))]
[JsonSerializable(typeof(ForgePromotionData))]
[JsonSerializable(typeof(ForgeVersionInfo))]
[JsonSerializable(typeof(NeoForgeVersionInfo))]
public partial class ForgeSeriesJsonContext : JsonSerializerContext { }

public class ForgePromotionData
{
    [JsonPropertyName("promos")]
    public Dictionary<string, string> Promos { get; set; } = [];
}

public class ForgeVersionInfo : IComparable<ForgeVersionInfo>
{
    public string FullVersionString { get; }
    public string MinecraftVersion { get; }
    public Version ForgeVersion { get; }

    public ForgeVersionInfo(string versionStr)
    {
        FullVersionString = versionStr ?? throw new ArgumentNullException(nameof(versionStr));
        int separatorIndex = versionStr.IndexOf('-');
        if (separatorIndex <= 0) throw new ArgumentException($"Invalid Forge version format: {versionStr}");

        MinecraftVersion = versionStr.Substring(0, separatorIndex);
        string forgePart = versionStr.Substring(separatorIndex + 1);

        if (!Version.TryParse(forgePart.Split('-')[0], out var parsedVersion))
            throw new ArgumentException($"Cannot parse Forge version part: {forgePart}");
        ForgeVersion = parsedVersion;
    }

    public int CompareTo(ForgeVersionInfo? other) => other == null ? 1 : ForgeVersion.CompareTo(other.ForgeVersion);
    public override string ToString() => FullVersionString;
}
public class NeoForgeVersionInfo : IComparable<NeoForgeVersionInfo>
{
    public string FullVersionString { get; }
    public Version ParsedNumericVersion { get; }
    public bool IsBeta { get; }

    public NeoForgeVersionInfo(string versionStr)
    {
        FullVersionString = versionStr ?? throw new ArgumentNullException(nameof(versionStr));
        string numericPart = versionStr;
        if (versionStr.Contains("-"))
        {
            numericPart = versionStr.Split('-')[0];
            IsBeta = versionStr.ToLowerInvariant().Contains("beta");
        }
        if (!Version.TryParse(numericPart, out var parsedVersion))
            throw new ArgumentException($"Cannot parse NeoForge version part: {numericPart}");
        ParsedNumericVersion = parsedVersion;
    }

    public int CompareTo(NeoForgeVersionInfo? other)
    {
        if (other == null) return 1;
        int numericCompare = ParsedNumericVersion.CompareTo(other.ParsedNumericVersion);
        if (numericCompare != 0) return numericCompare;
        return IsBeta == other.IsBeta ? 0 : (IsBeta ? -1 : 1); // 稳定版 (非Beta) 优先
    }
    public override string ToString() => FullVersionString;
}
// --- version.json 的模型 ---
public class ForgeSeriesVersionJson
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }
    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }
    [JsonPropertyName("mainClass")]
    public string MainClass { get; set; } = "";
    [JsonPropertyName("arguments")]
    public ForgeSeriesArguments? Arguments { get; set; }
    [JsonPropertyName("libraries")]
    public List<ForgeSeriesLibrary> Libraries { get; set; } = [];
}

public class ForgeSeriesArguments
{
    [JsonPropertyName("game")]
    public List<JsonElement> Game { get; set; } = [];
    [JsonPropertyName("jvm")]
    public List<JsonElement> Jvm { get; set; } = [];
}

// --- install_profile.json 的模型 ---

public class ForgeSeriesInstallProfile
{
    [JsonPropertyName("spec")]
    public int? Spec { get; set; }
    [JsonPropertyName("minecraft")]
    public string? Minecraft { get; set; }
    [JsonPropertyName("json")]
    public string? Json { get; set; }
    [JsonPropertyName("install")]
    public ForgeLegacyInstall? Install { get; set; }
    [JsonPropertyName("versionInfo")]
    public ForgeSeriesVersionJson? VersionInfo { get; set; }
    [JsonPropertyName("data")]
    public ForgeSeriesData? Data { get; set; }
    [JsonPropertyName("processors")]
    public List<ForgeSeriesProcessor> Processors { get; set; } = [];
    [JsonPropertyName("libraries")]
    public List<ForgeSeriesLibrary> Libraries { get; set; } = [];
}

public class ForgeLegacyInstall
{
    [JsonPropertyName("minecraft")]
    public string Minecraft { get; set; } = "";
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";
}

// 保留各代 profile 的客户端占位符。
public class ForgeSeriesData
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Placeholders { get; set; } = [];
}

// --- 通用子模型 ---
public class ForgeSeriesProcessor
{
    [JsonPropertyName("sides")]
    public List<string>? Sides { get; set; }
    [JsonPropertyName("jar")]
    public string Jar { get; set; } = "";
    [JsonPropertyName("classpath")]
    public List<string> Classpath { get; set; } = [];
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];
    [JsonPropertyName("outputs")]
    public Dictionary<string, string> Outputs { get; set; } = [];
}

public class ForgeSeriesLibrary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("clientreq")]
    public bool? ClientRequired { get; set; }
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    [JsonPropertyName("checksums")]
    public List<string> Checksums { get; set; } = [];
    [JsonPropertyName("rules")]
    public List<JsonElement>? Rules { get; set; }
    [JsonPropertyName("downloads")]
    public ForgeSeriesDownloads? Downloads { get; set; }
}

public class ForgeSeriesDownloads
{
    [JsonPropertyName("artifact")]
    public ForgeSeriesArtifact? Artifact { get; set; }
}

public class ForgeSeriesArtifact
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }
    [JsonPropertyName("size")]
    public int Size { get; set; }
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}
