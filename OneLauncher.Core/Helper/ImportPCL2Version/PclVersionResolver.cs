using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Mod.ModLoader.forgeseries;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OneLauncher.Core.Helper.ImportPCL2Version;

internal sealed record PclVersionMetadata(string InstancePath, string MinecraftRoot, string VersionJsonPath,
    string Name, string MinecraftVersion, ModEnum ModLoader, string? LoaderVersion, string? ClientJarPath,
    JsonObject Manifest, IReadOnlyList<PclRuntimeResource> RuntimeResources);

internal sealed record PclRuntimeResource(string RelativePath, int Size, string Sha1);

internal static class PclVersionResolver
{
    internal static async Task<PclVersionMetadata> ReadAsync(string instancePath, CancellationToken token)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancePath));
        if (!Directory.Exists(source)) throw new OlanException("导入失败", "所选版本目录不存在。");
        PclImportPaths.RequireRegular(source);
        if (!string.Equals(Directory.GetParent(source)?.Name, "versions", StringComparison.OrdinalIgnoreCase))
            throw new OlanException("导入失败", "请选择 .minecraft/versions 下的单个版本目录。");
        string root = Directory.GetParent(source)?.Parent?.FullName
            ?? throw new OlanException("导入失败", "无法确定 PCL 的 Minecraft 根目录。");
        string name = Path.GetFileName(source);
        string jsonPath = FindJson(source, name);
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var chain = new List<(string Directory, JsonObject Json)>();
        JsonObject merged = await ReadChainAsync(jsonPath, root, visited, chain, token);
        string? minecraft = Text(merged, "clientVersion") ?? GameArgument(merged, "--fml.mcVersion");
        if (minecraft == null)
            minecraft = chain.Select(item => Text(item.Json, "id")).LastOrDefault(value => Version.TryParse(value, out _));
        if (string.IsNullOrWhiteSpace(minecraft))
            throw new OlanException("导入失败", "无法从版本 JSON、继承链或加载器参数识别 Minecraft 版本。");
        PclImportPaths.Resolve(root, $"versions/{minecraft}");
        var (loader, loaderVersion) = DetectLoader(merged);
        if (Text(merged, "mainClass") == null || merged["assetIndex"] is not JsonObject || merged["libraries"] is not JsonArray)
            throw new OlanException("导入失败", "版本配置缺少主类、资源索引或依赖库。请先在 PCL 中补全该版本。");
        if (merged["arguments"]?["game"] is not JsonArray { Count: > 0 } && Text(merged, "minecraftArguments") == null)
            throw new OlanException("导入失败", "版本配置缺少游戏启动参数，请先在 PCL 中补全该版本。");
        NormalizeLibraries(merged, root);
        if (merged["javaVersion"] == null)
        {
            if (!Version.TryParse(minecraft, out var version))
                throw new OlanException("导入失败", "该版本缺少 Java 需求信息，请先在 PCL 中更新版本配置。");
            int major = version >= new Version(1, 20, 5) ? 21 : version >= new Version(1, 18) ? 17 : version >= new Version(1, 17) ? 16 : 8;
            merged["javaVersion"] = new JsonObject { ["majorVersion"] = major };
        }
        string? jarPath = null;
        foreach (var item in chain)
        {
            string? jarId = Text(item.Json, "jar");
            string candidate = jarId != null
                ? PclImportPaths.Resolve(root, $"versions/{jarId}/{jarId}.jar")
                : PclImportPaths.Resolve(item.Directory, Path.GetFileName(item.Directory) + ".jar");
            if (File.Exists(candidate)) { jarPath = candidate; break; }
        }
        jarPath ??= PclImportPaths.Resolve(root, $"versions/{minecraft}/{minecraft}.jar");
        if (merged["downloads"]?["client"] is not JsonObject)
        {
            if (!File.Exists(jarPath)) throw new OlanException("导入失败", "原实例缺少游戏主 JAR，也没有可用于补全的下载信息。");
            merged["downloads"] = new JsonObject { ["client"] = new JsonObject { ["size"] = new FileInfo(jarPath).Length } };
        }
        // GameData 保留可比较的原版版本号，独立 launch.json 保存所有 loader 补丁。
        merged["id"] = minecraft;
        var runtimeResources = await ReadForgeResourcesAsync(merged, root, minecraft, loader, loaderVersion, token);
        return new(source, root, jsonPath, name, minecraft, loader, loaderVersion, jarPath, merged, runtimeResources);
    }

    private static async Task<IReadOnlyList<PclRuntimeResource>> ReadForgeResourcesAsync(JsonObject manifest,
        string root, string minecraft, ModEnum loader, string? loaderVersion, CancellationToken token)
    {
        if (loader != ModEnum.forge || string.IsNullOrWhiteSpace(loaderVersion)) return [];
        string version = loaderVersion.StartsWith(minecraft + "-", StringComparison.Ordinal)
            ? loaderVersion : $"{minecraft}-{loaderVersion}";
        string libraries = PclImportPaths.Resolve(root, "libraries");
        string forgeRoot = PclImportPaths.Resolve(libraries, "net/minecraftforge");
        var declared = manifest["libraries"]!.AsArray()
            .Select(node => node?["downloads"]?["artifact"]?["path"]?.GetValue<string>())
            .Where(path => path != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resources = new List<PclRuntimeResource>();

        // Forge 按版本在 Maven 目录中另行定位这些文件，不能把它们并入普通 classpath。
        // 只遍历精确版本；不迁移其他 Forge 版本、安装器、服务端或源码包。
        if (Directory.Exists(forgeRoot))
        {
            foreach (string artifactDirectory in Directory.EnumerateDirectories(forgeRoot).Order())
            {
                string artifact = Path.GetFileName(artifactDirectory);
                string directory = PclImportPaths.Resolve(forgeRoot, $"{artifact}/{version}");
                if (!Directory.Exists(directory)) continue;
                foreach (string path in Directory.EnumerateFiles(directory, "*.jar").Order())
                {
                    token.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(path);
                    if (name != $"{artifact}-{version}.jar" &&
                        (artifact != "forge" || name != $"forge-{version}-client.jar" && name != $"forge-{version}-universal.jar")) continue;
                    PclImportPaths.RequireWithin(libraries, path);
                    string relative = Path.GetRelativePath(libraries, path).Replace('\\', '/');
                    if (declared.Contains(relative)) continue;
                    await using var stream = File.OpenRead(path);
                    resources.Add(new(relative, checked((int)stream.Length),
                        Convert.ToHexString(await SHA1.HashDataAsync(stream, token))));
                }
            }
        }

        var required = ForgeRuntimeLayout.RequiredImportedFiles(minecraft, loaderVersion);
        if (required.Count > 0)
        {
            var available = resources.Select(resource => resource.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in required)
            {
                if (!declared.Contains(relative) && !available.Contains(relative))
                    throw new OlanException("导入失败", $"原实例缺少 Forge 运行文件：{relative}。请先在 PCL 中补全该精确版本。");
            }
        }
        return resources;
    }

    private static void NormalizeLibraries(JsonObject manifest, string root)
    {
        foreach (JsonNode? node in manifest["libraries"]!.AsArray())
        {
            if (node is not JsonObject library || Text(library, "name") is not { } coordinate)
                throw new OlanException("导入失败", "依赖库缺少 Maven 坐标。");
            string[] parts = coordinate.Split(':');
            if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
                throw new OlanException("导入失败", $"依赖库 Maven 坐标无效：{coordinate}");
            if (library["downloads"]?["artifact"] is JsonObject artifact)
            {
                if (Text(artifact, "path") == null) artifact["path"] = MavenPath(root, coordinate);
                continue;
            }
            // 历史版本只有 name/url，安装好的库仍必须迁移，不能因缺 downloads 而跳过。
            string relative = MavenPath(root, coordinate);
            string mavenRoot = Text(library, "url") ?? (parts[0] == "net.minecraftforge" ? "https://maven.minecraftforge.net/" : "https://libraries.minecraft.net/");
            var descriptor = new JsonObject { ["path"] = relative, ["url"] = mavenRoot.TrimEnd('/') + "/" + relative };
            if (library["checksums"] is JsonArray checksums && checksums.FirstOrDefault() is JsonValue value &&
                value.TryGetValue<string>(out var sha1) && sha1.Length == 40 && sha1.All(Uri.IsHexDigit))
                descriptor["sha1"] = sha1;
            var downloads = library["downloads"] as JsonObject ?? new JsonObject();
            downloads["artifact"] = descriptor;
            library["downloads"] = downloads;
        }
    }

    private static string MavenPath(string root, string coordinate)
    {
        string libraries = Path.Combine(root, "libraries");
        string path = Tools.MavenToPath(libraries, coordinate);
        PclImportPaths.RequireWithin(libraries, path);
        return Path.GetRelativePath(libraries, path).Replace('\\', '/');
    }

    private static string FindJson(string directory, string name)
    {
        string expected = PclImportPaths.Resolve(directory, name + ".json");
        if (File.Exists(expected)) return expected;
        string conventional = PclImportPaths.Resolve(directory, "version.json");
        if (File.Exists(conventional)) return conventional;
        throw new OlanException("导入失败", $"未找到版本配置：{name}.json 或 version.json。请选择 .minecraft/versions 下的单个版本目录。");
    }

    private static async Task<JsonObject> ReadChainAsync(string jsonPath, string root, HashSet<string> visited,
        List<(string Directory, JsonObject Json)> chain, CancellationToken token)
    {
        if (!visited.Add(jsonPath) || visited.Count > 32)
            throw new OlanException("导入失败", "版本继承链循环或超过 32 层。");
        JsonObject json;
        try
        {
            await using var stream = File.OpenRead(jsonPath);
            json = await JsonNode.ParseAsync(stream, cancellationToken: token) as JsonObject
                ?? throw new JsonException("版本配置不是 JSON 对象");
        }
        catch (JsonException ex) { throw new OlanException("导入失败", $"版本 JSON 格式错误：{Path.GetFileName(jsonPath)}", OlanExceptionAction.Error, ex); }
        chain.Add((Path.GetDirectoryName(jsonPath)!, json));
        string? parent = Text(json, "inheritsFrom");
        if (parent == null) return (JsonObject)json.DeepClone();
        string parentDirectory = PclImportPaths.Resolve(root, $"versions/{parent}");
        var inherited = await ReadChainAsync(FindJson(parentDirectory, parent), root, visited, chain, token);
        foreach (var field in json)
        {
            if (field.Key == "libraries" && field.Value is JsonArray childLibraries)
            {
                var libraries = inherited["libraries"] as JsonArray ?? new JsonArray();
                foreach (var library in childLibraries)
                {
                    string? key = LibraryKey(library);
                    for (int i = libraries.Count - 1; i >= 0; i--)
                        if (key != null && LibraryKey(libraries[i]) == key) libraries.RemoveAt(i);
                    libraries.Add(library?.DeepClone());
                }
                inherited["libraries"] = libraries;
            }
            else if (field.Key == "arguments" && field.Value is JsonObject childArguments)
            {
                var arguments = inherited["arguments"] as JsonObject ?? new JsonObject();
                foreach (var argument in childArguments)
                {
                    var values = arguments[argument.Key] as JsonArray ?? new JsonArray();
                    if (argument.Value is JsonArray childValues)
                        foreach (var value in childValues) values.Add(value?.DeepClone());
                    arguments[argument.Key] = values;
                }
                inherited["arguments"] = arguments;
            }
            else if (field.Value != null) inherited[field.Key] = field.Value.DeepClone();
        }
        inherited.Remove("inheritsFrom");
        return inherited;
    }

    private static string? LibraryKey(JsonNode? library)
    {
        string? name = library?["name"]?.GetValue<string>();
        if (name == null) return null;
        string[] parts = name.Split(':');
        return parts.Length >= 3 ? $"{parts[0]}:{parts[1]}:{(parts.Length > 3 ? parts[3] : "")}" : name;
    }

    private static (ModEnum Loader, string? Version) DetectLoader(JsonObject json)
    {
        var names = (json["libraries"] as JsonArray)?.Select(node => node?["name"]?.GetValue<string>() ?? "").ToList() ?? [];
        string? coordinate = names.FirstOrDefault(name => name.StartsWith("net.neoforged:neoforge:", StringComparison.Ordinal) || name.StartsWith("net.neoforged:forge:", StringComparison.Ordinal));
        if (coordinate != null) return (ModEnum.neoforge, GameArgument(json, "--fml.neoForgeVersion") ?? coordinate.Split(':')[2]);
        coordinate = names.FirstOrDefault(name => name.StartsWith("net.minecraftforge:forge:", StringComparison.Ordinal));
        if (coordinate != null || GameArgument(json, "--fml.forgeVersion") != null)
            return (ModEnum.forge, GameArgument(json, "--fml.forgeVersion") ?? coordinate?.Split(':')[2]);
        coordinate = names.FirstOrDefault(name => name.StartsWith("org.quiltmc:quilt-loader:", StringComparison.Ordinal));
        if (coordinate != null || Text(json, "mainClass")?.Contains("org.quiltmc.", StringComparison.Ordinal) == true)
            return (ModEnum.quilt, coordinate?.Split(':')[2]);
        coordinate = names.FirstOrDefault(name => name.StartsWith("net.fabricmc:fabric-loader:", StringComparison.Ordinal));
        if (coordinate != null || Text(json, "mainClass")?.Contains("net.fabricmc.", StringComparison.Ordinal) == true)
            return (ModEnum.fabric, coordinate?.Split(':')[2]);
        return (ModEnum.none, null);
    }

    private static string? GameArgument(JsonObject json, string key)
    {
        if (json["arguments"]?["game"] is not JsonArray args) return null;
        for (int i = 0; i + 1 < args.Count; i++)
            if (args[i] is JsonValue value && value.TryGetValue<string>(out var text) && text == key)
                return args[i + 1]?.GetValue<string>();
        return null;
    }

    private static string? Text(JsonObject json, string key) => json[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
