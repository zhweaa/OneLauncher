using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;

internal static class GetShaderPackModrinth
{
    public static async Task<NdDowItem> GetDownloadInfoAsync(
        string projectId,
        string versionId,
        string shaderPackPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        // 只按 Minecraft 版本筛选，不以实例的 Mod 加载器或已安装依赖限制光影包。
        string gameVersions = Uri.EscapeDataString(JsonSerializer.Serialize(
            new[] { versionId }, ModrinthGetJsonContext.Default.StringArray));
        string url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version?game_versions={gameVersions}";
        using var response = await Init.Download.UnityClient.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new OlanException("安装光影包失败", $"项目 {projectId} 返回了无效的版本列表。");
        if (document.RootElement.GetArrayLength() == 0)
            throw new OlanException("安装光影包失败", $"项目 {projectId} 没有适配 Minecraft {versionId} 的光影包版本。");

        // Modrinth 版本列表按发布时间从新到旧返回；依赖字段只读取为元数据，不发起依赖请求。
        var version = document.RootElement[0].Deserialize(ModrinthGetJsonContext.Default.ModrinthProjects);
        var file = version?.Files?.FirstOrDefault(item => item.Primary) ?? version?.Files?.FirstOrDefault();
        if (file == null || !Uri.TryCreate(file.Url, UriKind.Absolute, out var downloadUri)
            || (downloadUri.Scheme != Uri.UriSchemeHttps && downloadUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new OlanException("安装光影包失败", $"项目 {projectId} 的版本缺少有效的下载文件。");
        }

        // 远端文件名只能是单个文件，防止写出 shaderpacks 目录。
        string filename = file.Filename;
        if (string.IsNullOrWhiteSpace(filename) || filename is "." or ".."
            || Path.IsPathRooted(filename) || filename.Contains('/') || filename.Contains('\\')
            || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new OlanException("安装光影包失败", $"项目 {projectId} 返回了无效的文件名。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new NdDowItem(file.Url, Path.Combine(shaderPackPath, filename), file.Size, file.Hashes?.Sha1);
    }
}
