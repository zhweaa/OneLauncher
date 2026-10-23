using System.IO.Compression;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;
using OneLauncher.Core.DefaultComponent.Resource.Pack;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;

internal static class GetModpackModrinth
{
    public static async Task<ModpackDownload> GetAsync(string projectId, CancellationToken cancellationToken)
    {
        string url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version";
        using var response = await Init.Download.UnityClient.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var metadata = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(metadata, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            throw new OlanException("获取整合包失败", $"项目 {projectId} 没有可安装的整合包版本。");

        var version = document.RootElement[0].Deserialize(ModrinthGetJsonContext.Default.ModrinthProjects);
        var files = version?.Files?.Where(file =>
            file.Filename?.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) == true).ToList();
        var file = files?.FirstOrDefault(file => file.Primary) ?? files?.FirstOrDefault();
        if (file == null || !Uri.TryCreate(file.Url, UriKind.Absolute, out var downloadUri)
            || (downloadUri.Scheme != Uri.UriSchemeHttps && downloadUri.Scheme != Uri.UriSchemeHttp))
            throw new OlanException("获取整合包失败", $"项目 {projectId} 缺少有效的 .mrpack 下载文件。");

        // 网络流先异步写入唯一临时文件，供 ZIP 随机读取和后续安装复用。
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"olan-pack-{Guid.NewGuid():N}.mrpack");
        try
        {
            using var packageResponse = await Init.Download.UnityClient.GetAsync(
                downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            packageResponse.EnsureSuccessStatusCode();
            await using (var networkStream = await packageResponse.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
                await networkStream.CopyToAsync(destination, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (Init.ConfigManager.Data.OlanSettings.IsSha1Enabled && !string.IsNullOrWhiteSpace(file.Hashes?.Sha1))
                await Init.Download.CheckAllSha1(
                    [new(file.Url, temporaryPath, file.Size, file.Hashes.Sha1)], 1, cancellationToken);

            using var archive = ZipFile.OpenRead(temporaryPath);
            var entry = archive.GetEntry("modrinth.index.json")
                ?? throw new OlanException("整合包无效", "压缩包内未找到 modrinth.index.json 文件。");
            using var manifestStream = entry.Open();
            var parser = new MrpackParser(manifestStream);
            // 提前读取侧栏与导入必需的信息，解析失败时清理下载文件。
            parser.GetMinecraftVersion();
            parser.GetLoaderInfo();
            cancellationToken.ThrowIfCancellationRequested();
            return new ModpackDownload(temporaryPath, parser);
        }
        catch
        {
            ModpackDownload.DeleteTemporaryFile(temporaryPath);
            throw;
        }
    }
}
