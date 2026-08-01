using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.ModService.Modrinth.JsonModelGet;
using System.Diagnostics;
using System.Text.Json;

namespace OneLauncher.Core.Net.ModService.Modrinth;

public class GetModrinth
{
    public ModrinthProjects info;
    public List<ModrinthProjects> dependencies;
    private readonly string modPath;
    private readonly string ModID;
    private readonly string version;
    private readonly ModEnum modType;
    public GetModrinth(string ModID, string version,ModEnum modType, string modPath)
    {
        this.modPath = modPath;
        this.ModID = ModID;
        this.version = version;
        this.modType = modType;
    }
    public async Task Init()
    {
        var client = Global.Init.Download.UnityClient; 
        var Url = $"https://api.modrinth.com/v2/project/{ModID}/version?game_versions=[\"{version}\"]&loaders=[\"{modType.ToString()}\"]";
        Debug.WriteLine($"[OneLauncher.Core.Net.ModService.Modrinth.GetModrinth.Init] 正在请求{Url}");
        HttpResponseMessage response = await client.GetAsync(Url);
        response.EnsureSuccessStatusCode();

        try
        {
            using (JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()))
            {
                JsonElement firstElement = document.RootElement[0];
                info = JsonSerializer.Deserialize<ModrinthProjects>(firstElement.GetRawText(),ModrinthGetJsonContext.Default.ModrinthProjects);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载或解析 Modrinth 主模组版本时出错: {ex.Message}");
            info = null;
            return;
        }

        if (info == null || info.Dependencies == null || info.Dependencies.Count == 0)
            return;

        this.dependencies = new List<ModrinthProjects>();
        foreach (var item in info.Dependencies)
        {
            var DUrl = $"https://api.modrinth.com/v2/project/{item.ProjectId}/version?game_versions=[\"{version}\"]&loaders=[\"{modType.ToString()}\"]";
            Debug.WriteLine($"[OneLauncher.Core.Net.ModService.Modrinth.GetModrinth.Init] 项目{item.ProjectId} 正在请求{DUrl}");
            HttpResponseMessage Dresponse = await client.GetAsync(DUrl);
            Dresponse.EnsureSuccessStatusCode();

            try
            {
                // **依赖模组版本处理：只获取第一个**
                using (JsonDocument dDocument = JsonDocument.Parse(await Dresponse.Content.ReadAsStringAsync()))
                {
                    JsonElement firstDependencyElement = dDocument.RootElement[0];
                    ModrinthProjects dependencyProject = JsonSerializer.Deserialize<ModrinthProjects>(firstDependencyElement.GetRawText(),ModrinthGetJsonContext.Default.ModrinthProjects)
                        ?? throw new InvalidOperationException("解析 Modrinth 依赖模组第一个版本失败。");
                    this.dependencies.Add(dependencyProject);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载或解析 Modrinth 依赖模组版本时出错 (ID: {item.ProjectId}): {ex.Message}");
            }
        }
    }
    public NdDowItem? GetDownloadInfos()
    {
        if (info == null)
            return null;
        return new NdDowItem(
            Url: info.Files[0].Url,
            Path: Path.Combine(modPath, info.Files[0].Filename),
            Size: info.Files[0].Size,
            Sha1: info.Files[0].Hashes.Sha1);
    }
    public List<NdDowItem> GetDependenciesInfos()
    {
        List<NdDowItem> items = new List<NdDowItem>();
        if (dependencies == null)
            return null;
        foreach (var item in dependencies)
        {
            items.Add(new NdDowItem(
                Url: item.Files[0].Url,
                Path: Path.Combine(modPath, item.Files[0].Filename),
                Size: item.Files[0].Size,
                Sha1: item.Files[0].Hashes.Sha1));
        }
        return items;
    }
}
