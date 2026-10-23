using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.ModService.Modrinth;

public enum ResourceType
{
    Mod,
    ShaderPack,
    Modpack
}

public class SearchModrinth
{
    public ModrinthSearch? info;
    private readonly HttpClient httpClient = Init.Download.UnityClient;

    /// <summary>按资源类型检索 Modrinth；空关键词返回热门项目。</summary>
    public async Task<ModrinthSearch> ToSearch(
        string Key,
        ResourceType resourceType = ResourceType.Mod,
        CancellationToken cancellationToken = default)
    {
        string facets = resourceType switch
        {
            ResourceType.Mod => """[["categories:neoforge","categories:fabric","categories:quilt"],["project_type:mod"]]""",
            ResourceType.ShaderPack => """[["project_type:shader"]]""",
            ResourceType.Modpack => """[["project_type:modpack"]]""",
            _ => throw new ArgumentOutOfRangeException(nameof(resourceType))
        };
        string searchUrl = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(Key ?? string.Empty)}&facets={Uri.EscapeDataString(facets)}&index=downloads";
        using var response = await httpClient.GetAsync(
            searchUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var jsonResponse = await response.Content.ReadAsStreamAsync(cancellationToken);
        info = await JsonSerializer.DeserializeAsync(
            jsonResponse, ModrinthSearchJsonContext.Default.ModrinthSearch, cancellationToken);
        return info ?? throw new OlanException("无法搜索资源信息", "Modrinth 返回了无效的搜索结果。");
    }
}

