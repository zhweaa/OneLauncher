using OneLauncher.Core.DefaultComponent.Mod.ModLoader.fabric;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.Fabric;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;
using OneLauncher.Core.DefaultComponent.Resource.ModService.Modrinth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders.ModSources;

internal class FabricProvider : IModLoaderConcreteProviders
{
    private readonly DownloadInfo _context;
    public FabricProvider(DownloadInfo context)
    {
        _context = context;
    }
    public async Task<List<NdDowItem>> GetDependencies(Action<string>? reportStatus = null)
    {
        reportStatus?.Invoke("正在获取 Fabric 加载器元数据...");
        List<NdDowItem> fabricDependencies;
        string fabricMetaFilePath = Path.Combine(
            _context.VersionInstallInfo.VersionPath,
            "version.fabric.json"
        );
        FabricRoot info;

        
        if (_context.SpecifiedFabricVersion != null) 
        {
            info = (await _context.SpecifiedFabricVersion.GetDownloadFiles())
                .Deserialize(FabricJsonContext.Default.FabricRoot)
                ?? throw new OlanException("内部错误", "无法解析Fabric文本");
        }
        // 如果没有指定Fabric版本，从API返回的第一个数据取最新的版本
        else
        {
            using Stream rep = await Init.Download.UnityClient
                .GetStreamAsync($"https://meta.fabricmc.net/v2/versions/loader/{_context.ID}");
            using JsonDocument document = JsonDocument.Parse(rep);
            JsonElement firstElement = document.RootElement[0];
            info = firstElement.Deserialize(FabricJsonContext.Default.FabricRoot)
                ?? throw new OlanException("内部错误", "无法解析Fabric文本");
        }
        // 写入到文件
        using (FileStream fs = new FileStream(fabricMetaFilePath, FileMode.Create, FileAccess.Write,FileShare.None,0,true))
            await JsonSerializer.SerializeAsync<FabricRoot>(fs,info,FabricJsonContext.Default.FabricRoot);
        // 预留，自定义版本
        var parser = new FabricVJParser(info,_context.GameRootPath);

        fabricDependencies = parser.GetLibraries();

        if (_context.IsDownloadFabricWithAPI)
        {
            reportStatus?.Invoke($"已收集 {fabricDependencies.Count} 个 Fabric 库条目，正在查询 Fabric API 及其依赖信息...");
            var modrinthTask = new GetModrinth(
                "fabric-api", 
                _context.ID,  
                ModEnum.fabric,
                Path.Combine(_context.UserInfo.InstancePath, "mods") 
            );
            await modrinthTask.Init();
            var fabricApiFile = modrinthTask.GetDownloadInfos();
            if (fabricApiFile.HasValue)
            {
                fabricDependencies.Add(fabricApiFile.Value);
            }
        }
        return fabricDependencies;
    }
}
