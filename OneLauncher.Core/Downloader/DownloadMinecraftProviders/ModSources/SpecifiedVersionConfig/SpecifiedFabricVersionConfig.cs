using OneLauncher.Core.Global;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;


namespace OneLauncher.Core.Downloader.DownloadMinecraftProviders.ModSources.SpecifiedVersionConfig;
/*
 开发中功能
 目的：让玩家可以在下载游戏时指定模组加载器版本
 路径：
 页面(DownloadPaneViewModel） -> 下载引导 （DownloadInfo）-> 下载实际（DownloadMinecraft.CreateDownloadPlan） -> 对应加载器的支持库提供类（FabricProvider）
 */
public class SpecifiedFabricVersionConfig
{
    private JsonArray? node;
    private string? loaderVersion;
    /// <summary>
    /// UI界面使用这个来获取可用版本列表
    /// （必须调用）
    /// </summary>
    /// <param name="version">游戏版本</param>
    public async Task<List<string>> GetVersions(string version)
    {
        using Stream rep = await Init.Download.unityClient
            .GetStreamAsync($"https://meta.fabricmc.net/v2/versions/loader/{version}");
        node = (await JsonNode.ParseAsync(rep))?.Root?.AsArray() ?? throw new OlanException("内部错误", "无法解析Fabric文本");
        List<string> versions = new List<string>();
        for(int i = 0; i < node.Count; i++)
        {
            versions.Add(node[i]!["loader"]!["version"]!.GetValue<string>());
        }
        return versions;
    }
    /// <summary>
    /// UI界面使用这个来设置指定的Fabric版本
    /// （必须调用）
    /// </summary>
    /// <param name="version">指定的模组加载器版本</param>
    public void SetLoaderVersion(string version)
    {
        loaderVersion = version;
    }
    /// <summary>
    /// 后端使用这个来获取指定版本的Json范围
    /// </summary>
    public async Task<JsonNode> GetDownloadFiles()
    {
        if (node == null || loaderVersion == null)
            throw new OlanException("内部错误", "内部数值未初始化");
        for (int i = 0; i < node.Count; i++)
        {
            string p = node[i]!["loader"]!["version"]!.GetValue<string>();
            if (p == loaderVersion)
            {
                return node[i]!;
            }
        }
        throw new OlanException("内部错误", "未找到指定的Fabric版本");
    }
}
