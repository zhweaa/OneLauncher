using OneLauncher.Core.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Minecraft.JsonModels;
using System.Security.Cryptography;
using System.Text.Json;

namespace OneLauncher.Core.Mod.ModLoader.forgeseries;

internal static class ForgeMappingsDownloader
{
    internal static async Task<bool> TryDownloadAsync(Download download, string processorJar,
        IReadOnlyList<string> args, string versionJsonPath, Action<string>? output, CancellationToken token)
    {
        // 只接管已确认是原始文件复制的任务；sanitize/format 等变体仍由 Java 原处理器执行。
        if (processorJar != "net.minecraftforge:installertools:1.4.1")
            return false;
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "--skipIfExists")
                continue;
            if (args[i] is not ("--task" or "--version" or "--side" or "--output") ||
                i + 1 >= args.Count || !options.TryAdd(args[i], args[++i]))
                return false;
        }
        if (options.Count != 4 || options["--task"] != "DOWNLOAD_MOJMAPS" || options["--side"] != "client")
            return false;
        if (!File.Exists(versionJsonPath))
            return false;

        await using var metadataStream = File.OpenRead(versionJsonPath);
        var metadata = await JsonSerializer.DeserializeAsync(metadataStream,
            MinecraftJsonContext.Default.MinecraftVersionInfo, token);
        var mappings = metadata?.Downloads?.ClientMappings;
        if (metadata?.ID != options["--version"] || mappings?.Size is not > 0 ||
            string.IsNullOrEmpty(mappings.Url) || string.IsNullOrEmpty(mappings.Sha1))
            return false;

        string destination = options["--output"];
        try
        {
            if (File.Exists(destination) && new FileInfo(destination).Length == mappings.Size)
            {
                await using var existing = File.OpenRead(destination);
                string hash = Convert.ToHexString(await SHA1.HashDataAsync(existing, token));
                if (hash.Equals(mappings.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    output?.Invoke("Mojang 映射缓存 SHA1 校验通过，无需 Java 联网。");
                    return true;
                }
            }

            output?.Invoke("使用启动器下载 Mojang 映射并校验 SHA1...");
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await download.DownloadFileAndSha1(mappings.Url, destination, mappings.Sha1, token);
                    break;
                }
                catch (HttpRequestException) when (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 << attempt), token);
                }
            }
            if (new FileInfo(destination).Length != mappings.Size)
                throw new InvalidDataException("Mojang 映射文件大小与版本元数据不一致。");
            output?.Invoke("Mojang 映射下载完成，SHA1 校验通过。");
            return true;
        }
        catch (HttpRequestException ex)
        {
            throw new OlanException("Mojang 映射下载失败", $"下载映射文件失败：{ex.Message}", OlanExceptionAction.Error, ex);
        }
    }
}
