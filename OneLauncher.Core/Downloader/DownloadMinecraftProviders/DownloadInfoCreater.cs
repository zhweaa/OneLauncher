using OneLauncher.Core.Downloader.DownloadMinecraftProviders.ModSources.SpecifiedVersionConfig;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Downloader.DownloadMinecraftProviders;

public partial class DownloadInfo
{
    private DownloadInfo() { }
    public static async Task<DownloadInfo> Create(
        string versionId,
        ModType modType,
        // 下面是一些下载选项
        bool isAllowToUseBetaNeoforge = false,
        bool isUseRecommendedToInstallForge = false,
        bool isDownloadFabricWithAPI = true,
        bool isDownloadWithJavaRuntime = true,
        bool isDownloadQuiltWhitQSL = true,
        // 模组加载器版本配置，若不传递则按上述规则获取
        // （代码预留）
        SpecifiedFabricVersionConfig? fabricConfig = null,
        // 下面是一些可传递可不传递的参数，不传递会自动获取
        VersionBasicInfo? versionBasic = null,
        GameData? gameDataD = null,
        string? gameRootPathD = null
        )
    {
        var download = Init.Download;
        string gameRootPath = gameRootPathD ?? Init.GameRootPath;
        #region 查找一些资源
        // 创建默认实例
        ModEnum modEnum = modType.ToModEnum();
        string defaultInstanceModLoaderDisplayName =
            modEnum == ModEnum.fabric
            ? "fabric"
            : modEnum == ModEnum.neoforge
            ? "neoforge"
            : modEnum == ModEnum.forge
            ? "forge"
            : modEnum == ModEnum.quilt
            ? "quilt"
            : "原版";
        string defaultInstanceName = $"{versionId} - {defaultInstanceModLoaderDisplayName}";
        GameData gameData = gameDataD ?? new GameData(defaultInstanceName, versionId, modEnum, Init.AccountManager.GetDefaultUser().UserID);

        // 确定下载信息
        VersionBasicInfo versionDownloadInfo =
            versionBasic ??
            Init.MojangVersionList.FirstOrDefault(x => x.ID == versionId)
            ?? throw new OlanException("内部错误", "无法搜索到你需要下载版本的下载信息");

        // 确定版本信息
        UserVersion userVersion = new()
        {
            VersionID = versionId,
            modType = modType,
            AddTime = DateTime.Now,
        };
        #endregion
        #region 补全可能没有的资源
        VersionInfomations mations;

        var versionJsonSavePath = Path.Combine(userVersion.VersionPath, "version.json");

        if (!File.Exists(versionJsonSavePath))
            await download.DownloadFile(versionDownloadInfo.Url, versionJsonSavePath);
        mations = new VersionInfomations(
            await File.ReadAllTextAsync(versionJsonSavePath),
            gameRootPath
            );
        #endregion

        AppConfig config = Init.ConfigManager.Data;
        return new DownloadInfo
        {
            DownloadTool = download,
            VersionMojangInfo = mations,

            VersionInstallInfo = userVersion,
            UserInfo = gameData,
            VersionDownloadInfo = versionDownloadInfo,

            IsAllowToUseBetaNeoforge = isAllowToUseBetaNeoforge,
            IsDownloadFabricWithAPI = isDownloadFabricWithAPI,
            IsUseRecommendedToInstallForge = isUseRecommendedToInstallForge,
            ISDownloadQuiltWhitQSL = isDownloadQuiltWhitQSL,
            AndJava = isDownloadWithJavaRuntime,

            // 预留的模组加载器版本配置
            SpecifiedFabricVersion = fabricConfig,

            GameRootPath = gameRootPath,

            MaxDownloadThreads = Math.Clamp(config.OlanSettings.MaximumDownloadThreads, 1, 256),
            MaxSha1Threads = Math.Clamp(config.OlanSettings.MaximumSha1Threads, 1, 256),
            IsSha1 = config.OlanSettings.IsSha1Enabled,
            DownloadStrategy = config.OlanSettings.DownloadMinecraftSourceStrategy
        };
    }
}
