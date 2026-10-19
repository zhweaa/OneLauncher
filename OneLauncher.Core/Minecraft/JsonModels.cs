using OneLauncher.Core.Helper.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneLauncher.Core.Minecraft.JsonModels;
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftVersionInfo))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftAssetIndex))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftDownloads))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftDownloadUrl))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftLibrary))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftLibraryDownloads))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftLibraryArtifact))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftArguments))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftArgument))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftRule))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.Os))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftNatives))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftLogging))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftLoggingClient))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftLoggingFile))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.JavaVersion))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftVersionList))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftLatestList))]
[JsonSerializable(typeof(OneLauncher.Core.Minecraft.JsonModels.MinecraftAllVersionInfomations))]
[JsonSerializable(typeof(System.Collections.Generic.List<OneLauncher.Core.Minecraft.JsonModels.MinecraftLibrary>))]
[JsonSerializable(typeof(System.Collections.Generic.List<OneLauncher.Core.Minecraft.JsonModels.MinecraftRule>))]
[JsonSerializable(typeof(System.Collections.Generic.List<object>))]
[JsonSerializable(typeof(System.Collections.Generic.List<string>))]
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, OneLauncher.Core.Minecraft.JsonModels.MinecraftLibraryArtifact>))]
[JsonSerializable(typeof(System.Collections.Generic.List<VersionBasicInfo>))]
public partial class MinecraftJsonContext : JsonSerializerContext { }
// 表示version.json的顶层结构
public class MinecraftVersionInfo
{
    [JsonPropertyName("assetIndex")]
    public MinecraftAssetIndex? AssetIndex { get; set; }

    [JsonPropertyName("downloads")]
    public MinecraftDownloads? Downloads { get; set; }

    [JsonPropertyName("libraries")]
    public List<MinecraftLibrary>? Libraries { get; set; }

    [JsonPropertyName("arguments")]
    public MinecraftArguments? Arguments { get; set; }

    [JsonPropertyName("mainClass")]
    public string? MainClass { get; set; }

    [JsonPropertyName("logging")]
    public MinecraftLogging? Logging { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersion? JavaVersion { get; set; }
    [JsonPropertyName("id")]
    public string? ID { get; set; }
}

// 表示Java版本信息
public class JavaVersion
{
    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }
}

// 表示资源索引文件下载
public class MinecraftAssetIndex
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } // 可以为空

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; } // 可以为空

    [JsonPropertyName("url")]
    public string? Url { get; set; } // 可以为空

    [JsonPropertyName("size")]
    public int? Size { get; set; }
}

// 表示主文件下载信息
public class MinecraftDownloads
{
    [JsonPropertyName("client")]
    public MinecraftDownloadUrl? Client { get; set; }
    [JsonPropertyName("server")]
    public MinecraftDownloadUrl? Server { get; set; }
    [JsonPropertyName("client_mappings")]
    public MinecraftDownloadUrl? ClientMappings { get; set; }
}

// 表示主文件的下载信息和地址
public class MinecraftDownloadUrl
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
    [JsonPropertyName("size")]
    public int? Size { get; set; }
}

// 表示单个库信息（顶层）
public class MinecraftLibrary
{
    [JsonPropertyName("downloads")]
    public MinecraftLibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("rules")]
    public List<MinecraftRule>? Rules { get; set; }

    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }
}

// 表示库的下载信息
public class MinecraftLibraryDownloads
{
    // 普通库文件
    [JsonPropertyName("artifact")]
    public MinecraftLibraryArtifact? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, MinecraftLibraryArtifact>? Classifiers { get; set; }
}

// 表示普通库文件的具体下载信息
public class MinecraftLibraryArtifact
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
    [JsonPropertyName("size")]
    public int? Size { get; set; }
}
// 表示启动参数（jvm和game）
public class MinecraftArguments
{
    [JsonPropertyName("jvm")]
    [JsonConverter(typeof(JvmArgumentConverter))] // 使用自定义转换器处理混合类型
    public List<object>? Jvm { get; set; } // object 可以是 string 或 MinecraftArgument
}

// JVM 参数列表的自定义 JSON 转换器，处理 string 和 MinecraftArgument 对象
public class JvmArgumentConverter : JsonConverter<List<object>>
{
    public override List<object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<object>();
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("JVM 参数应为数组");

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                list.Add(reader.GetString() ?? ""); // 处理可能的 null 字符串
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // 反序列化为 MinecraftArgument 对象
                var arg = JsonSerializer.Deserialize(ref reader, MinecraftJsonContext.Default.MinecraftArgument);
                if (arg != null) list.Add(arg);
            }
            else
            {
                throw new JsonException($"JVM 参数中遇到未知 Token 类型: {reader.TokenType}");
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<object> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            if (item is string str)
                writer.WriteStringValue(str);
            else if (item is MinecraftArgument arg)
                JsonSerializer.Serialize(writer, arg, MinecraftJsonContext.Default.MinecraftArgument); // 序列化为 MinecraftArgument
            else
                // 处理其他类型或抛出异常
                throw new JsonException($"JVM 参数中遇到未知对象类型: {item?.GetType().Name}");
        }
        writer.WriteEndArray();
    }
}
// 表示单个启动参数，可能包含 rules
public class MinecraftArgument
{
    [JsonPropertyName("rules")]
    public List<MinecraftRule>? Rules { get; set; } // 可以为空

    [JsonPropertyName("value")]
    [JsonConverter(typeof(ArgumentValueConverter))] // 使用自定义转换器处理 string 或 List<string>
    public object? Value { get; set; } // 值可以是 string 或 List<string>，可以为空
}
// 表示规则
public class MinecraftRule
{
    [JsonPropertyName("action")]
    public string? Action { get; set; } // action 可以是 "allow" 或 "disallow"，可以为空

    [JsonPropertyName("os")]
    public Os? Os { get; set; } // 操作系统要求，可以为空
}

// 表示操作系统要求
public class Os
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }
}

// 表示natives库的操作系统映射
public class MinecraftNatives
{
    [JsonPropertyName("linux")]
    public string? Linux { get; set; } // Linux 原生库分类器键，可以为空

    [JsonPropertyName("osx")]
    public string? Osx { get; set; } // macOS 原生库分类器键，可以为空

    [JsonPropertyName("windows")]
    public string? Windows { get; set; } // Windows 原生库分类器键，可以为空
}

// 表示日志配置信息
public class MinecraftLogging
{
    [JsonPropertyName("client")]
    public MinecraftLoggingClient? Client { get; set; } // 客户端日志配置，可以为空
}

// 表示客户端日志配置
public class MinecraftLoggingClient
{
    [JsonPropertyName("argument")]
    public string? Argument { get; set; } // 日志参数字符串，可以为空

    [JsonPropertyName("file")]
    public MinecraftLoggingFile? File { get; set; } // 日志配置文件信息，可以为空
}

// 表示日志配置文件下载地址
public class MinecraftLoggingFile
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } // 文件 ID，可以为空

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; } // 文件 SHA1 哈希，可以为空

    [JsonPropertyName("url")]
    public string? Url { get; set; } // 文件下载 URL，可以为空
    [JsonPropertyName("size")]
    public int? Size { get; set; } // 文件大小，可以为空
}
// 自定义 JSON 转换器，用于处理 argument.value（string 或 List<string>）
public class ArgumentValueConverter : JsonConverter<object>
{
    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString() ?? ""; // 处理可能的 null 字符串
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    list.Add(reader.GetString() ?? ""); // 处理可能的 null 字符串
                }
                else
                {
                    // 如果数组中包含非字符串项，根据需要处理或抛出异常
                    // 根据 Minecraft version.json 规范，value 数组中的项通常是字符串。
                    throw new JsonException($"参数值数组中遇到未知 Token 类型: {reader.TokenType}");
                }
            }
            return list;
        }
        // 根据规范，value 可能还支持数字、布尔值等其他类型，如果遇到需要根据实际情况添加处理。
        throw new JsonException($"无效的参数值 Token 类型: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value is string str)
            writer.WriteStringValue(str);
        else if (value is List<string> list)
        {
            writer.WriteStartArray();
            foreach (var item in list)
                writer.WriteStringValue(item);
            writer.WriteEndArray();
        }
        else
        {
            // 处理其他类型或抛出异常
            throw new JsonException($"无效的参数值对象类型: {value?.GetType().Name}");
        }
    }
}

public class MinecraftVersionList
{
    [JsonPropertyName("latest")]
    public MinecraftLatestList latest { get; set; }
    [JsonPropertyName("versions")]
    public List<MinecraftAllVersionInfomations> AllVersions { get; set; }
}
public class MinecraftLatestList
{
    [JsonPropertyName("release")]
    public string release { get; set; }
    [JsonPropertyName("snapshot")]
    public string snapshot { get; set; }
}
public class MinecraftAllVersionInfomations
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    [JsonPropertyName("type")]
    public string Type { get; set; }
    [JsonPropertyName("url")]
    public string Url { get; set; }
    [JsonPropertyName("releaseTime")]
    public DateTime Time { get; set; }

}
