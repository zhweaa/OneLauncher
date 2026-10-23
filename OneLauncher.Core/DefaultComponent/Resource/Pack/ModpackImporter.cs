using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Resource.Pack;

/// <summary>
/// 负责导入 Modrinth (.mrpack) 整合包的核心类。
/// 它实现了 IDisposable 接口，以确保临时文件能够被正确清理。
/// </summary>
public class ModpackImporter : IDisposable
{
    private static readonly SemaphoreSlim InstanceRegistrationLock = new(1, 1);
    private readonly string _gameRootPath;
    private readonly GameDataManager _gameDataManager;
    private readonly DBManager _dbManager;
    private readonly VersionListManager _versionListManager;
    private readonly MrpackParser? _parser;
    private string _tempWorkDir; // 临时解压目录

    /// <summary>
    /// 私有构造函数，防止外部直接实例化。
    /// </summary>
    private ModpackImporter(string gameRootPath, GameDataManager gameDataManager, DBManager dbManager, MrpackParser? parser = null)
    {
        _gameRootPath = gameRootPath;
        _gameDataManager = gameDataManager;
        _dbManager = dbManager;
        _parser = parser;
        _versionListManager = UnitedCore.Core.VersionListManager;
        _versionListManager.EnsureGameRoot(gameRootPath);
        _tempWorkDir = Path.Combine(Path.GetTempPath(), $"olan-import-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_tempWorkDir);
    }

    /// <summary>
    /// [静态入口] 从指定的 .mrpack 文件路径开始导入流程。
    /// </summary>
    /// <param name="packPath">.mrpack 文件的完整路径。</param>
    /// <param name="gameRoot">游戏根目录 (.minecraft) 的路径。</param>
    /// <param name="progress">用于报告进度的回调。</param>
    /// <param name="token">用于取消操作的 CancelToken。</param>
    public static async Task<GameData> ImportFromMrpackAsync(
        string packPath,
        string gameRoot,
        IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress,
        CancellationToken token = default,
        MrpackParser? parser = null)
    {
        // 依赖注入所需的管理器
        var gameDataManager = UnitedCore.Core.GameDataManager;
        var dbManager = Init.ConfigManager;

        // 使用 using 确保下载器和导入器资源被释放
        using var importer = new ModpackImporter(gameRoot, gameDataManager, dbManager, parser);

        return await importer.RunImportAsync(packPath, progress, token);
    }

    /// <summary>
    /// 执行整个导入流程的核心方法。
    /// </summary>
    private async Task<GameData> RunImportAsync(string packPath, IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress, CancellationToken token)
    {
        // --- 步骤 1: 解压与解析清单文件 ---
        progress.Report((DownProgress.Meta, 0, 0, "正在解压整合包..."));
        ZipFile.ExtractToDirectory(packPath, _tempWorkDir);
        token.ThrowIfCancellationRequested();

        string manifestPath = Path.Combine(_tempWorkDir, "modrinth.index.json");
        if (!File.Exists(manifestPath))
            throw new OlanException("整合包无效", "压缩包内未找到 modrinth.index.json 文件。", OlanExceptionAction.Error);

        await using var manifestStream = File.OpenRead(manifestPath);
        var parser = _parser ?? new MrpackParser(manifestStream);

        // --- 步骤 2: 创建游戏实例 ---
        progress.Report((DownProgress.Meta, 0, 0, "正在创建游戏实例..."));
        (ModEnum loaderType, string loaderVersion) = parser.GetLoaderInfo();
        var packGameData = new GameData(
            name: parser.GetName(),
            versionId: parser.GetMinecraftVersion(),
            loader: loaderType,
            userModel: UnitedCore.Core.AccountManager.GetDefaultUser().UserID // 默认为当前用户
        );
        // 仅回滚本次创建的目录；固定绝对路径并排除已有实例。
        string instanceRoot = Path.GetFullPath(Path.Combine(_gameRootPath, "instance"));
        string instancePath = Path.GetFullPath(packGameData.InstancePath);
        if (!string.Equals(Path.GetDirectoryName(instancePath), instanceRoot, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(instancePath) || File.Exists(instancePath)
            || _gameDataManager.GetInstanceFromId(packGameData.InstanceId) != null)
            throw new OlanException("创建实例失败", $"实例目录无效或已存在：{instancePath}");

        bool registered = false;
        bool registrationLockHeld = false;
        try
        {
            Directory.CreateDirectory(instancePath);

            // --- 步骤 3: 先应用覆盖文件，使其可参与清单文件查重 ---
            await InstallModpackFilesAsync(parser, packGameData, progress, token);
            token.ThrowIfCancellationRequested();

            // --- 步骤 4: 仅在基础游戏或指定加载器版本缺失时安装 ---
            if (!IsBaseGameAndLoaderInstalled(packGameData, loaderType, loaderVersion))
                await InstallBaseGameAsync(packGameData, progress, token);
            token.ThrowIfCancellationRequested();

            // --- 步骤 5: 注册新的游戏实例并保存 ---
            // 登记和失败回滚串行执行，避免并行导入在保存期间修改同一实例字典。
            await InstanceRegistrationLock.WaitAsync(token);
            registrationLockHeld = true;
            await _gameDataManager.AddGameDataAsync(packGameData);
            registered = true;
            progress.Report((DownProgress.Done, 1, 1, "整合包导入成功！"));
            return packGameData;
        }
        catch
        {
            if (!registered)
            {
                // AddGameDataAsync 在保存前更新内存；保存失败也要撤销本次登记。
                if (ReferenceEquals(_gameDataManager.GetInstanceFromId(packGameData.InstanceId), packGameData))
                {
                    try { await _gameDataManager.RemoveGameDataAsync(packGameData.InstanceId); }
                    catch (Exception ex) { Trace.TraceWarning($"[ModpackImporter] 撤销实例登记失败：{ex.Message}"); }
                }
                try
                {
                    if (Directory.Exists(instancePath)) Directory.Delete(instancePath, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"[ModpackImporter] 清理未完成的实例目录 '{instancePath}' 失败：{ex.Message}");
                }
            }
            throw;
        }
        finally
        {
            if (registrationLockHeld) InstanceRegistrationLock.Release();
        }
    }

    /// <summary>
    /// 负责下载和安装 Minecraft 基础版和指定的 Mod 加载器。
    /// </summary>
    private async Task InstallBaseGameAsync(GameData gameData, IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress, CancellationToken token)
    {
        var downInfo = await DownloadInfo.Create(
            versionId: gameData.VersionId,
            modType: new ModType { IsFabric = gameData.ModLoader == ModEnum.fabric, IsNeoForge = gameData.ModLoader == ModEnum.neoforge, IsForge = gameData.ModLoader == ModEnum.forge, IsQuilt = gameData.ModLoader == ModEnum.quilt },
            defaultGameData: gameData
        );

        await UnitedCore.Core.VersionInstallerService.InstallNewVersion(downInfo, progress, token);
    }

    /// <summary>
    /// 负责下载所有 Mod 文件并应用覆盖文件 (overrides)。
    /// </summary>
    private async Task InstallModpackFilesAsync(MrpackParser parser, GameData gameData, IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress, CancellationToken token)
    {
        // --- 应用 Overrides before checking the manifest files ---
        progress.Report((DownProgress.Meta, 0, 0, "正在应用覆盖文件..."));
        string overridesSourceDir = Path.Combine(_tempWorkDir, "overrides");
        if (Directory.Exists(overridesSourceDir))
            await Tools.CopyDirectoryAsync(overridesSourceDir, gameData.InstancePath, token);

        // --- 下载清单中目标文件尚不存在的文件 ---
        var filesToDownload = parser.GetModFiles(gameData.InstancePath)
            .Where(file => !File.Exists(file.path))
            .ToList();

        if (filesToDownload.Any())
        {
            int totalMods = filesToDownload.Count;
            int downloadedMods = 0;
            var modProgress = new Progress<(int, string)>(p =>
            {
                downloadedMods++;
                progress.Report((DownProgress.DownAndInstModFiles, totalMods, downloadedMods, p.Item2));
            });

            await Init.Download.DownloadListAsync(modProgress, filesToDownload, _dbManager.Data.OlanSettings.MaximumDownloadThreads, token);
        }

    }

    private bool IsBaseGameAndLoaderInstalled(GameData gameData, ModEnum loaderType, string loaderVersion)
    {
        var installedVersion = _versionListManager.FindVersion(gameData.VersionId);
        if (installedVersion == null)
            return false;
        bool hasLoader = loaderType switch
        {
            ModEnum.none => true,
            ModEnum.fabric => installedVersion.modType.IsFabric,
            ModEnum.quilt => installedVersion.modType.IsQuilt,
            ModEnum.forge => installedVersion.modType.IsForge,
            ModEnum.neoforge => installedVersion.modType.IsNeoForge,
            _ => false
        };
        if (!hasLoader) return false;

        string loaderMetadataPath = Path.Combine(_gameRootPath, "versions", gameData.VersionId,
            loaderType switch
            {
                ModEnum.fabric => "version.fabric.json",
                ModEnum.quilt => "version.quilt.json",
                ModEnum.forge => "version.forge.json",
                ModEnum.neoforge => "version.neoforge.json",
                _ => "version.json"
            });

        if (loaderType == ModEnum.none)
            return File.Exists(loaderMetadataPath);
        if (!File.Exists(loaderMetadataPath))
            return false;

        using var metadata = JsonDocument.Parse(File.ReadAllText(loaderMetadataPath));
        return ContainsLoaderVersion(metadata.RootElement, loaderVersion);
    }

    private static bool ContainsLoaderVersion(JsonElement element, string loaderVersion)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("version") && property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() == loaderVersion)
                    return true;
                if (property.NameEquals("id") && property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString()!.Contains(loaderVersion, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (ContainsLoaderVersion(property.Value, loaderVersion))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (ContainsLoaderVersion(item, loaderVersion))
                    return true;
        }
        return false;
    }

    /// <summary>
    /// 清理临时文件和目录。
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (!string.IsNullOrEmpty(_tempWorkDir) && Directory.Exists(_tempWorkDir))
            {
                Directory.Delete(_tempWorkDir, true);
            }
        }
        catch (IOException ex)
        {
            // 记录日志，但不抛出异常，以免影响程序关闭
            Debug.WriteLine($"[ModpackImporter] 清理临时目录 '{_tempWorkDir}' 失败: {ex.Message}");
        }
    }
}
