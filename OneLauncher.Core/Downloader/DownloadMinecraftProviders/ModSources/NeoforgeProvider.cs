using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Mod.ModLoader.forgeseries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Downloader.DownloadMinecraftProviders.ModSources;

internal class NeoforgeProvider : IModLoaderConcreteProviders
{
    private readonly DownloadInfo _context;
    private readonly ForgeSeriesInstallTasker _installTasker;

    // 用于在 GetDependenciesAsync 和 RunPostInstallTasksAsync 之间传递数据
    private string _clientLzmaTempPath;
    public NeoforgeProvider(DownloadInfo context)
    {
        _context = context;
        // 提前创建安装任务器实例，供后续两个方法使用
        _installTasker = new ForgeSeriesInstallTasker(
            _context.DownloadTool,
            Path.Combine(_context.GameRootPath, "libraries"),
            _context.GameRootPath
        );
    }
    public async Task<List<NdDowItem>> GetDependencies()
    {
        #region 确定安装器Url
        string installerUrl;
        if (!string.IsNullOrEmpty(_context.SpecifiedNeoForgeVersion))
            installerUrl = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{_context.SpecifiedNeoForgeVersion}/neoforge-{_context.SpecifiedNeoForgeVersion}-installer.jar";
        else
        {
            // 自动获取最新版本
            installerUrl = await new ForgeVersionListGetter(_context.DownloadTool.UnityClient)
                .GetInstallerUrlAsync(
                    false,
                    _context.ID,
                    _context.IsAllowToUseBetaNeoforge,
                    _context.IsUseRecommendedToInstallForge
                );
        }
        #endregion

        // 调用准备方法
        (List<NdDowItem> versionLibs, List<NdDowItem> installerLibs, string lzmaPath) = await _installTasker.StartReadyAsync(
            installerUrl,
            "NeoForge",
            _context.ID
        );
        _clientLzmaTempPath = lzmaPath;
        // 合并下载信息
        return versionLibs.Concat(installerLibs).ToList();
    }

    public async Task RunInstaller(IProgress<string> Put, CancellationToken token)
    {
        if (_clientLzmaTempPath == null)
            throw new OlanException("内部错误", "无法执行安装器，无法得到补丁文件");
        _installTasker.ProcessorsOutEvent += (all, done, message) =>
        {
            if (all == -1 && done == -1)
                throw new OlanException("执行Neoforge安装器时出错", $"当安装器[{done}/{all}]被执行时抛出了错误或异常退出{Environment.NewLine}错误信息：{message}");
            Put?.Report($"[执行处理器({done}/{all})]{Environment.NewLine}{message}");
        };

        await _installTasker.RunProcessorsAsync(
            _context.VersionMojangInfo.GetMainFile().path,
            Init.JavaManager.GetJavaExecutablePath(_context.VersionMojangInfo.GetJavaVersion()),
            _clientLzmaTempPath,
            token,
            isForge: _context.UserInfo.ModLoader == ModEnum.forge
        );

        // 清理临时文件
        if (File.Exists(_clientLzmaTempPath))
            File.Delete(_clientLzmaTempPath);

    }
}