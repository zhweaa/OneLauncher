using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Legacy;

/// <summary>旧 DEOBF_REALMS 隐式依赖原版清单及 libraries 布局，在本次工作目录中补全上下文。</summary>
internal static class ForgeRealmsProcessor
{
    public static bool Applies(ForgeSeriesProcessor processor) =>
        processor.Jar.StartsWith("net.minecraftforge:installertools:", StringComparison.Ordinal) && processor.Args.Contains("DEOBF_REALMS");

    public static async Task<ForgeSeriesProcessor> PrepareAsync(ForgeSeriesProcessor processor,
        string originalJar, string work, Download download, CancellationToken token)
    {
        var source = Path.Combine(Path.GetDirectoryName(originalJar)!, "version.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(source, token));
        var root = document.RootElement;
        var json = Path.Combine(work, "minecraft.json");
        await File.WriteAllTextAsync(json, root.GetRawText(), token);
        var libraries = Path.Combine(work, "libraries");
        Directory.CreateDirectory(libraries);
        if (root.TryGetProperty("libraries", out var items))
            foreach (var item in items.EnumerateArray())
            {
                var coordinate = item.GetProperty("name").GetString()!;
                if (!coordinate.StartsWith("com.mojang:realms:", StringComparison.Ordinal)) continue;
                var artifact = item.GetProperty("downloads").GetProperty("artifact");
                var staged = ForgePaths.Under(libraries, artifact.GetProperty("path").GetString()!);
                var hash = artifact.GetProperty("sha1").GetString()!;
                var size = artifact.GetProperty("size").GetInt64();
                if (!await ForgeFileVerifier.ValidAsync(staged, [hash], size, token))
                    await download.DownloadFile(artifact.GetProperty("url").GetString()!, staged, token);
                await ForgeFileVerifier.RequireAsync(staged, [hash], size, token);
            }
        // 官方工具支持显式 --json/--libs，避免它推导出工作目录以外的路径。
        var args = processor.Args.ToList();
        if (args.Contains("--json") || args.Contains("--libs"))
            throw new OlanException("Forge Realms 配置不受支持", "DEOBF_REALMS 具有自定义目录参数。");
        args.AddRange(["--json", "{ROOT}/minecraft.json", "--libs", "{LIBRARY_DIR}"]);
        return new ForgeSeriesProcessor { Jar = processor.Jar, Classpath = processor.Classpath, Args = args, Outputs = processor.Outputs, Sides = processor.Sides };
    }
}
