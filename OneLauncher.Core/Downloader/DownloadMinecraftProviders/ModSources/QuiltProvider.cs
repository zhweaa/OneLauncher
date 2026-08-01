using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Mod.ModLoader.fabric.quilt;
using OneLauncher.Core.ModLoader.fabric.JsonModels;
using OneLauncher.Core.Net.ModService.Modrinth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneLauncher.Core.Downloader.DownloadMinecraftProviders.ModSources;
internal class QuiltProvider : IModLoaderConcreteProviders
{
    private readonly DownloadInfo _context;
    public QuiltProvider(DownloadInfo context)
    {
        _context = context;
    }
    public async Task<List<NdDowItem>> GetDependencies()
    {
        List<NdDowItem> quiltDependencies;
        string quiltMetaFilePath = Path.Combine(
            _context.VersionInstallInfo.VersionPath,
            "version.quilt.json"
        );

        // 从 Quilt Meta 获取加载器信息
        using Stream rep = await Init.Download.UnityClient
            .GetStreamAsync($"https://meta.quiltmc.org/v3/versions/loader/{_context.ID}");
        using JsonDocument document = JsonDocument.Parse(rep);
        JsonElement firstElement = document.RootElement[0];
        var info = firstElement.Deserialize(FabricJsonContext.Default.FabricRoot)
        ?? throw new OlanException("内部错误", "无法解析Quilt元数据");

        // 写入到文件
        using (FileStream fs = new FileStream(quiltMetaFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 0, true))
            await JsonSerializer.SerializeAsync<FabricRoot>(fs, info, FabricJsonContext.Default.FabricRoot);

        var parser = new QuiltNJParser(info, _context.GameRootPath);

        quiltDependencies = parser.GetLibraries();

        // Quilt 同样可以有 API (Quilted Fabric API / QSL)
        if (_context.ISDownloadQuiltWhitQSL) // 复用这个选项
        {
            var modrinthTask = new GetModrinth(
                "qsl", // Quilt Standard Libraries (QSL)
                _context.ID,
                ModEnum.quilt,
                Path.Combine(_context.UserInfo.InstancePath, "mods")
            );
            await modrinthTask.Init();
            var quiltApiFile = modrinthTask.GetDownloadInfos();
            if (quiltApiFile.HasValue)
            {
                quiltDependencies.Add(quiltApiFile.Value);
            }
        }
        return quiltDependencies;
    }
}