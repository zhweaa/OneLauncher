using OneLauncher.Core.Net.ModService.Modrinth.JsonModelSearch;
using System.Diagnostics;
using System.Text.Json;
using OneLauncher.Core.Global;
namespace OneLauncher.Core.Net.ModService.Modrinth;
public class SearchModrinth
{
    public ModrinthSearch? info;
    private readonly HttpClient httpClient = Init.Download.unityClient; // 如果不需求高安全性身份验证就用这个
    public SearchModrinth()
    {
    }
    public async Task<ModrinthSearch> ToSearch(string Key)
    {
        // 搜索仅限支持fabric或支持neoforge的模组
        string searchUrl = $"https://api.modrinth.com/v2/search?query=\"{Key}\"&facets=[[\"categories:neoforge\",\"categories:fabric\",\"categories:quilt\"],[\"project_type:mod\"]]&index=downloads";
        if (string.IsNullOrEmpty(Key))
            searchUrl = $"https://api.modrinth.com/v2/search?query=&facets=[[\"categories:neoforge\",\"categories:fabric\",\"categories:quilt\"],[\"project_type:mod\"]]&index=downloads";
        HttpResponseMessage response = await httpClient.GetAsync(searchUrl);
        response.EnsureSuccessStatusCode();

        Stream jsonResponse = await response.Content.ReadAsStreamAsync();

        // 使用带有选项的源生成器反序列化
        info = await JsonSerializer.DeserializeAsync<ModrinthSearch>(jsonResponse,ModrinthSearchJsonContext.Default.ModrinthSearch);

        return info ?? throw new OlanException("无法搜索模型信息",$"服务器地址‘{searchUrl}’结果无法被序列化");
    }
}
