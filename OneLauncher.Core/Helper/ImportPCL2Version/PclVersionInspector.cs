using System.Text.Json;
using System.Text.RegularExpressions;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Launch;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.Helper.ImportPCL2Version;

/// <summary>读取实例及继承版本的识别证据，不根据用户自定义版本名猜加载器。</summary>
internal static class PclVersionInspector
{
    internal sealed record Result(string Minecraft, string Name, string MainClass, ModEnum ModLoader);

    public static async Task<Result> ReadAsync(string jsonPath, CancellationToken token)
    {
        var versionFolder = Path.GetDirectoryName(Path.GetFullPath(jsonPath))!;
        var versionsFolder = Directory.GetParent(versionFolder)?.FullName
            ?? throw new OlanException("导入失败", "无法确定 versions 目录。");
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var libraries = new List<string>();
        var arguments = new List<string>();
        string? mainClass = null, minecraft = null, name = null;
        var path = Path.GetFullPath(jsonPath);
        for (var depth = 0; ; depth++)
        {
            if (depth >= 32 || !visited.Add(path)) throw new OlanException("导入失败", "版本继承链循环或过深。");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
            var root = document.RootElement;
            string? Text(string key) => root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            mainClass ??= Text("mainClass");
            name ??= Text("id") ?? Path.GetFileName(versionFolder);
            minecraft ??= Text("clientVersion");
            if (root.TryGetProperty("libraries", out var libs))
                foreach (var lib in libs.EnumerateArray())
                    if (lib.TryGetProperty("name", out var library)) libraries.Add(library.GetString()!);
            arguments.AddRange(ForgeLaunchArguments.Tokenize(Text("minecraftArguments") ?? ""));
            if (root.TryGetProperty("arguments", out var args) && args.TryGetProperty("game", out var game))
                arguments.AddRange(ForgeLaunchArguments.Expand(game.EnumerateArray()));
            var parent = Text("inheritsFrom");
            if (parent == null)
            {
                minecraft ??= Text("id");
                break;
            }
            if (!Global.ModelDataMangers.VersionListManager.IsValidVersionId(parent))
                throw new OlanException("导入失败", $"无效的父版本 ID：{parent}");
            var folder = Path.Combine(versionsFolder, parent);
            path = Path.Combine(folder, parent + ".json");
            if (!File.Exists(path)) path = Path.Combine(folder, "version.json");
            if (!File.Exists(path)) throw new OlanException("导入失败", $"缺少父版本配置：{path}");
        }
        // 一些独立清单用 fml 参数记录原版版本，没有 clientVersion/inheritsFrom。
        var mcIndex = arguments.IndexOf("--fml.mcVersion");
        if (mcIndex >= 0 && mcIndex + 1 < arguments.Count &&
            (minecraft == null || !Version.TryParse(minecraft, out _))) minecraft = arguments[mcIndex + 1];
        var knownVersion = minecraft != null && (Version.TryParse(minecraft, out _) ||
            Regex.IsMatch(minecraft, @"^(\d{2}w\d{2}[a-z]|[abc]\d[\d._a-z-]*|rd-\d+|inf-\d+)$", RegexOptions.CultureInvariant));
        if (!knownVersion || !Global.ModelDataMangers.VersionListManager.IsValidVersionId(minecraft ?? ""))
            throw new OlanException("导入失败", "无法确定有效的 Minecraft 基础版本。");
        if (string.IsNullOrWhiteSpace(mainClass)) throw new OlanException("导入失败", "版本继承链中缺少启动主类。");
        return new(minecraft!, name!, mainClass, Tools.MainClassToModEnum(mainClass, libraries, arguments));
    }
}
