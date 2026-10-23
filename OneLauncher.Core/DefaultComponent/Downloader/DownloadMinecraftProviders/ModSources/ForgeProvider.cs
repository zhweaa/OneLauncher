using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.JarMod;

namespace OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders.ModSources;

internal class ForgeProvider : IModLoaderConcreteProviders
{
    private readonly DownloadInfo _context;
    private readonly ForgeSeriesInstallTasker _installTasker;

    // 同一实例保留准备结果，供依赖下载完成后的安装阶段使用。
    public ForgeProvider(DownloadInfo context)
    {
        _context = context;
        // 提前创建安装任务器实例，供后续两个方法使用
        _installTasker = new ForgeSeriesInstallTasker(
            _context.DownloadTool,
            Path.Combine(_context.GameRootPath, "libraries"),
            _context.GameRootPath
        );
    }
    public async Task<List<NdDowItem>> GetDependencies(Action<string>? reportStatus = null)
    {
        JarModSupport.RequireInstaller(_context.ID);
        reportStatus?.Invoke("正在确定 Forge 版本及安装包地址...");
        string installerUrl;
        if (!string.IsNullOrEmpty(_context.SpecifiedForgeVersion))
            installerUrl = ForgeVersionListGetter.GetArtifactUrl(true, _context.ID, _context.SpecifiedForgeVersion);
        else
        {
            // 自动获取最新版本
            installerUrl = await new ForgeVersionListGetter(_context.DownloadTool.UnityClient)
                .GetInstallerUrlAsync(
                    true,
                    _context.ID,
                    _context.IsAllowToUseBetaNeoforge,
                    _context.IsUseRecommendedToInstallForge
                );
        }

        // 调用准备方法
        return await _installTasker.StartReadyAsync(
            installerUrl,
            "Forge",
            _context.ID,
            reportStatus
        );
    }

    public async Task RunInstaller(IProgress<string> progress, CancellationToken token)
    {
        void Report(int all, int done, string message)
        {
            progress.Report($"[执行处理器({done}/{all})]{Environment.NewLine}{message}");
        }
        _installTasker.ProcessorsOutEvent += Report;
        try
        {
            await _installTasker.RunProcessorsAsync(
                _context.VersionMojangInfo.GetMainFile().path,
                UnitedCore.Core.JavaManager.GetJavaExecutablePath(_context.VersionMojangInfo.GetJavaVersion()), token);
        }
        finally { _installTasker.ProcessorsOutEvent -= Report; }
    }
}
