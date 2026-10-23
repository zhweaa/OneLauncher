using OneLauncher.Core.DefaultComponent.Resource;
using OneLauncher.Core.DefaultComponent.Resource.ModService.Modrinth;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;
using OneLauncher.Core.DefaultComponent.Resource.Pack;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.Services;

public sealed class ResourceInstallerService
{
    internal ResourceInstallerService()
    {
    }

    /// <summary>获取最新 .mrpack 的网络流并解析信息；调用方负责释放返回值。</summary>
    public Task<ModpackDownload> GetModpackInfoAsync(string projectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        cancellationToken.ThrowIfCancellationRequested();
        return GetModpackModrinth.GetAsync(projectId, cancellationToken);
    }

    /// <summary>复用预览时的包文件及解析器导入新实例，取消令牌沿既有安装流程传入 DownloadMinecraft。</summary>
    public async Task<GameData> ImportModpack(
        ModpackDownload package,
        IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        return await ModpackImporter.ImportFromMrpackAsync(
            package.FilePath, Init.GameRootPath, progress, cancellationToken, package.Parser);
    }

    /// <summary>
    /// 从 Modrinth 安装适配实例 Minecraft 版本的最新光影包到 shaderpacks。
    /// 不检查或安装 Iris、OptiFine 等依赖，也不自动启用光影包。
    /// </summary>
    /// <param name="progress">文件数量、百分比和阶段进度；允许为 null。</param>
    /// <param name="shaderPackId">Modrinth 光影项目 ID 或 slug。</param>
    /// <param name="instance">用于确定 Minecraft 版本和安装目录的目标实例。</param>
    /// <param name="verifySha1">是否校验服务端提供的 SHA1，包括已存在的文件。</param>
    /// <param name="cancellationToken">取消元数据获取、下载及校验。</param>
    /// <exception cref="OneLauncher.Core.Global.OlanException">没有兼容版本或下载文件信息无效。</exception>
    public async Task InstallShaderPack(
        IProgress<(long AllFiles, long DownedFiles, double Percentage, string DowingFileName)>? progress,
        string shaderPackId,
        GameData instance,
        bool verifySha1 = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderPackId);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report((0, 0, 0, "正在获取光影包版本信息..."));
        var file = await GetShaderPackModrinth.GetDownloadInfoAsync(
            shaderPackId,
            instance.VersionId,
            Path.Combine(instance.InstancePath, "shaderpacks"),
            cancellationToken);

        await ResourceFileInstaller.InstallAsync(
            [file], progress, "光影包", verifySha1, cancellationToken);
    }

    public async Task InstallMod(
        IProgress<(long AllFiles, long DownedFiles,double Percentage, string DowingFileName)> progress,
        string modId,
        GameData instance,
        ModEnum modType,
        bool includeDependencies = true,
        bool verifySha1 = true,
        CancellationToken cancellationToken = default)
    {
        var modPath = Path.Combine(instance.InstancePath, "mods");
        var GetTask = new GetModrinth(modId,instance.VersionId, modType, modPath);
        await GetTask.Init(cancellationToken, includeDependencies);

        // 获取主 Mod 文件信息
        NdDowItem? mainMod = GetTask.GetDownloadInfos();
        if (!mainMod.HasValue)
            throw new OneLauncher.Core.Global.OlanException(
                "安装模组失败", $"未找到项目 {modId} 适配 Minecraft {instance.VersionId} 和 {modType} 的模组版本。");

        List<NdDowItem> allFiles = new List<NdDowItem> { (NdDowItem)mainMod };

        // 如果需要下载依赖项，则获取依赖项信息并添加到下载列表
        if (includeDependencies)
        {
            List<NdDowItem> dependencies = GetTask.GetDependenciesInfos();
            if (dependencies != null)
                allFiles.AddRange(dependencies);
        }

        await ResourceFileInstaller.InstallAsync(
            allFiles, progress, "Mod", verifySha1, cancellationToken);
    }
}
