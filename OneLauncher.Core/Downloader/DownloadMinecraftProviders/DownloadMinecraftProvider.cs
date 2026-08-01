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
public partial class DownloadMinecraft
{
    private readonly JavaManager _javaManager = Init.JavaManager;
    private readonly RaceDownloader _raceDownloader = new RaceDownloader(Init.Download.UnityClient);
    private Task DownloadClientTasker(NdDowItem main)
    {
        NdDowItem[] downloadSources = _urlProviders.Select(p => p.GetClientMainFile(main)).ToArray();
        return _raceDownloader.RaceSingleFileAsync(
            downloadSources,
            cancelToken
        );
    }
    private Task DownloadAssetsSupportTasker(List<NdDowItem> assets)
    {
        IEnumerable<NdDowItem> allPossibleDownloads = _urlProviders
            .SelectMany(provider => provider.GetAssetsFiles(assets));
        IEnumerable<IGrouping<string, NdDowItem>> groupedByFile = allPossibleDownloads
            .GroupBy(item => item.path);
        return _raceDownloader.RaceManyFilesAsync(
            groupedByFile,
            info.MaxDownloadThreads,
            new Progress<string>(fileName =>
            {
                progress?.Report((DownProgress.DownAssets, alls, Interlocked.Increment(ref dones), fileName));
            }),
            cancelToken
        );
    }

    private async Task DownloadLibrariesSupportTasker(List<NdDowItem> libraries)
    {
        IEnumerable<NdDowItem> allPossibleDownloads = _urlProviders
            .SelectMany(provider => provider.GetLibrariesFiles(libraries));
        IEnumerable<IGrouping<string, NdDowItem>> groupedByFile = allPossibleDownloads
            .GroupBy(item => item.path);
        await _raceDownloader.RaceManyFilesAsync(
            groupedByFile,
            info.MaxDownloadThreads,
            new Progress<string>(fileName =>
            {
                progress?.Report((DownProgress.DownLibs, alls, Interlocked.Increment(ref dones), fileName));
            }),
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
                new Progress<(int a, string b)>(p =>
                {
                    Interlocked.Increment(ref dones);
                    progress?.Report((DownProgress.DownAndInstModFiles, alls, dones, p.b));
                }),
                modNds, info.MaxDownloadThreads, cancelToken);
        // 处理器需要Java
        await javaInstallTask;
        // 依次执行每个加载器的处理器
        foreach (var provider in modProviders)
            await provider.RunInstaller(
                new Progress<string>(p =>
                {
                    progress?.Report((DownProgress.DownAndInstModFiles, alls, dones, p));
                }), cancelToken);
        
    }
    private Task LogginInstallTasker(NdDowItem log4j2_xml)
    {
        Interlocked.Increment(ref dones);
        return info.DownloadTool.DownloadFile(log4j2_xml.url, log4j2_xml.path, cancelToken);
    }
    private Task JavaInstallTasker() => 
        _javaManager.InstallJava(info.VersionMojangInfo.GetJavaVersion(), JavaProvider.Adoptium, token: cancelToken);
    
}
