using OneLauncher.Core.Downloader.DownloadMinecraftProviders.ModSources;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Mod.ModLoader.forgeseries;
using OneLauncher.Core.Net;
using OneLauncher.Core.Net.JavaProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Downloader.DownloadMinecraftProviders;
public enum DownProgress
{
    Meta,
    DownMain,
    DownLibs,
    DownAndInstModFiles,
    DownAssets,
    DownLog4j2,
    Verify,
    Done
}
internal partial class DownloadMinecraft
{
    private readonly JavaManager _javaManager = Init.JavaManager;
    private RaceDownloader RaceDownloader => new(info.DownloadTool.UnityClient);
    private async Task DownloadClientTasker(NdDowItem main)
    {
        NdDowItem[] downloadSources = _urlProviders.Select(p => p.GetClientMainFile(main)).ToArray();
        await RaceDownloader.RaceSingleFileAsync(
            downloadSources,
            cancelToken
        );
        if (!cancelToken.IsCancellationRequested)
            ReportDownloadCompletion(DownProgress.DownMain, main.path, Path.GetFileName(main.path));
    }
    private Task DownloadAssetsSupportTasker(List<NdDowItem> assets)
    {
        IEnumerable<NdDowItem> allPossibleDownloads = _urlProviders
            .SelectMany(provider => provider.GetAssetsFiles(assets));
        IEnumerable<IGrouping<string, NdDowItem>> groupedByFile = allPossibleDownloads
            .GroupBy(item => item.path);
        return RaceDownloader.RaceManyFilesAsync(
            groupedByFile,
            info.MaxDownloadThreads,
            new InlineProgress<string>(path => ReportDownloadCompletion(DownProgress.DownAssets, path, Path.GetFileName(path))),
            cancelToken
        );
    }

    private async Task DownloadLibrariesSupportTasker(List<NdDowItem> libraries)
    {
        IEnumerable<NdDowItem> allPossibleDownloads = _urlProviders
            .SelectMany(provider => provider.GetLibrariesFiles(libraries));
        IEnumerable<IGrouping<string, NdDowItem>> groupedByFile = allPossibleDownloads
            .GroupBy(item => item.path);
        await RaceDownloader.RaceManyFilesAsync(
            groupedByFile,
            info.MaxDownloadThreads,
            new InlineProgress<string>(path => ReportDownloadCompletion(DownProgress.DownLibs, path, Path.GetFileName(path))),
            cancelToken
        );

        // 解压原生库文件
        await Task.Run(() =>
        {
            foreach (var i in info.VersionMojangInfo.NativesLibs)
            {
                Download.ExtractFile(Path.Combine(info.GameRootPath, "libraries", i), Path.Combine(info.VersionInstallInfo.VersionPath, "natives"));
            }
        }, cancelToken);
    }
    private async Task UnityModsInstallTasker(List<NdDowItem> modNds, IModLoaderConcreteProviders[] modProviders,Task javaInstallTask)
    {
        // 先把依赖库下载了
        await info.DownloadTool.DownloadListAsync(
                modNds, info.MaxDownloadThreads, cancelToken,
                file => ReportDownloadCompletion(DownProgress.DownAndInstModFiles, file.path, file.url));
        // 处理器需要Java
        await javaInstallTask;
        // 依次执行每个加载器的处理器
        foreach (var provider in modProviders)
            await provider.RunInstaller(
                new InlineProgress<string>(p =>
                {
                    progress?.Report((DownProgress.DownAndInstModFiles, alls, dones, p));
                }), cancelToken);
        
    }
    private async Task LogginInstallTasker(NdDowItem log4j2_xml)
    {
        await info.DownloadTool.DownloadFile(log4j2_xml.url, log4j2_xml.path, cancelToken);
        ReportDownloadCompletion(DownProgress.DownLog4j2, log4j2_xml.path, Path.GetFileName(log4j2_xml.path));
    }
    private void ReportDownloadCompletion(DownProgress stage, string path, string message)
    {
        lock (_progressLock)
        {
            string key = Path.GetFullPath(path);
            if (!_pendingPaths.Contains(key) || !_completedPaths.Add(key))
                return;
            dones = _completedPaths.Count;
            progress?.Report((stage, alls, dones, message));
        }
    }
    private Task JavaInstallTasker() => 
        _javaManager.InstallJava(info.VersionMojangInfo.GetJavaVersion(), JavaProvider.Adoptium, token: cancelToken);
    
}
