using OneLauncher.Core.Downloader.DownloadMinecraftProviders.ModSources;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Downloader.DownloadMinecraftProviders;
internal partial class DownloadMinecraft
{
    internal readonly record struct DownloadPlan(
        List<NdDowItem> AllFilesGoVerify,
        List<NdDowItem> LibraryFiles,
        List<NdDowItem> AssetFiles,
        List<NdDowItem>? ModLoaderFiles,
        NdDowItem ClientMainFile,
        NdDowItem? LoggingFile,
        IModLoaderConcreteProviders[] ModProviders // 如果原版则为null
    );
    internal async Task<DownloadPlan> CreateDownloadPlan()
    {
        #region 原版资源
        var mation = info.VersionMojangInfo;
        // 依赖库和资源文件
        var libraryFiles = new List<NdDowItem>(mation.GetLibraries());
        var assetsIndex = mation.GetAssets();
        if (!File.Exists(assetsIndex.path))
            await info.DownloadTool.DownloadFile(assetsIndex.url,assetsIndex.path,cancelToken);
        var assetFiles = VersionAssetIndex.ParseAssetsIndex(
            await File.ReadAllTextAsync(assetsIndex.path, cancelToken),
            info.GameRootPath
        );
        // 一些别的
        var clientFile = mation.GetMainFile();
        var loggingFile = mation.GetLoggingConfig();
        #endregion

        #region 模组加载器

        List<IModLoaderConcreteProviders> providers = new();
        if (info.VersionInstallInfo.modType.IsFabric)
            providers.Add(new FabricProvider(info));
        if (info.VersionInstallInfo.modType.IsNeoForge)
            providers.Add(new NeoforgeProvider(info));
        if (info.VersionInstallInfo.modType.IsForge)
            providers.Add(new ForgeProvider(info));
        if (info.VersionInstallInfo.modType.IsQuilt) 
            providers.Add(new QuiltProvider(info));

        List<NdDowItem>? modFiles = new();
        #endregion

        #region 汇总所有文件
        var allFiles = new List<NdDowItem>(libraryFiles.Count+assetFiles.Count+2);
        allFiles.AddRange(assetFiles);
        allFiles.AddRange(libraryFiles);
        allFiles.Add(clientFile);
        if(loggingFile != null)
            allFiles.Add((NdDowItem)loggingFile);
        if (providers.Count != 0)
            foreach (var provider in providers)
            {
                var dependencies = await provider.GetDependencies();
                modFiles.AddRange(dependencies);
                allFiles.AddRange(dependencies);
            }
        
        return new DownloadPlan(
            // 自动检查文件存在性
            AllFilesGoVerify:info.DownloadTool.CheckFilesExists(allFiles,cancelToken),
            LibraryFiles: info.DownloadTool.CheckFilesExists(libraryFiles,cancelToken),
            AssetFiles: info.DownloadTool.CheckFilesExists(assetFiles,cancelToken),
            ModLoaderFiles: info.DownloadTool.CheckFilesExists(modFiles,cancelToken),
            ClientMainFile: clientFile,
            LoggingFile:loggingFile,
            ModProviders: providers.ToArray()
            );
        #endregion
    }
}
