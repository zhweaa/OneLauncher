using OneLauncher.Core.Compatible.ImportPCL2Version;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Core.Helper.ImportPCL2Version;

/// <summary>
/// 负责处理从 PCL2 实例文件夹导入游戏到 OneLauncher 的全部逻辑。
/// </summary>
public class PCL2Importer
{
    private readonly IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> _progress;
    private readonly CancellationToken _token;
    private readonly Download _downloader = Init.Download;

    private readonly GameDataManager _gameDataManager = UnitedCore.Core.GameDataManager;
    private readonly AccountManager _accountManager = UnitedCore.Core.AccountManager;

    public PCL2Importer(IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress, CancellationToken? token = null)
    {
        _progress = progress;
        _token = token ?? CancellationToken.None;
    }

    /// <summary>
    /// [入口方法] 开始从 PCL2 实例文件夹进行导入。
    /// </summary>
    /// <param name="pclInstancePath">PCL2 的单个版本实例文件夹路径 (例如 E:\mc\.minecraft\versions\1.21.1-Fabric 0.16.14)。</param>
    public async Task ImportAsync(string pclInstancePath)
    {
        _progress?.Report((DownProgress.Meta, 0, 0, "分析PCL2实例..."));

        // 1. 解析PCL2的 version.json 获取基础信息
        string pclVersionJsonPath = FindPclVersionJson(pclInstancePath);
        var pclInfo = await PclVersionInspector.ReadAsync(pclVersionJsonPath, _token);

        string mcVersion = pclInfo.Minecraft;
        string customName = pclInfo.Name;
        ModEnum modLoader = pclInfo.ModLoader;

        _progress?.Report((DownProgress.Meta, 0, 0, $"已识别: {customName} (MC: {mcVersion}, 加载器: {modLoader})"));

        // 2. 创建OneLauncher游戏实例
        var gameData = new GameData(customName, mcVersion, modLoader, _accountManager.GetDefaultUser().UserID);
        Directory.CreateDirectory(gameData.InstancePath);

        var versionsDir = new DirectoryInfo(pclInstancePath).Parent;
        string pclMinecraftRoot = versionsDir?.Parent?.FullName
                                  ?? throw new OlanException("导入失败", "无法确定PCL2的 .minecraft 根目录。");

        // 3. **核心改进：使用 Planner 制定计划**
        _progress?.Report((DownProgress.Meta, 0, 0, "正在规划文件迁移与下载..."));
        var downloadInfo = await DownloadInfo.Create(
            gameData.VersionId,
            new ModType { IsFabric = modLoader == ModEnum.fabric, IsNeoForge = modLoader == ModEnum.neoforge, IsForge = modLoader == ModEnum.forge, IsQuilt = modLoader == ModEnum.quilt },defaultGameData: gameData);

        // 4. **核心改进：执行文件迁移（本地优先）**
        //    创建一个临时的DownloadMinecraft实例，仅为调用其内部Planner
        var planner = new DownloadMinecraft(downloadInfo, _progress, _token);
        var plan = await planner.CreateDownloadPlan(); //

        //    从PCL2目录迁移所需文件，这会把文件复制到OneLauncher的目标目录
        await MigrateFilesFromPcl(plan.AllFilesGoVerify, pclMinecraftRoot, Init.GameRootPath);

        // 5. **核心改进：执行完整的下载和安装流程**
        //    现在文件已经就位，我们调用标准的下载/安装流程。
        //    它会自动跳过已存在的文件，并为Forge/NeoForge执行安装器。
        await UnitedCore.Core.VersionInstallerService.InstallNewVersion(downloadInfo, _progress, _token);

        // 6. 迁移用户数据 (Mods, Saves, etc.)
        _progress?.Report((DownProgress.Meta, 0, 0, "正在迁移用户数据..."));
        await MigratePclInstanceContentAsync(pclInstancePath, gameData.InstancePath); //

        // 7. 将实例添加到管理器并保存
        await _gameDataManager.AddGameDataAsync(gameData);
        _progress?.Report((DownProgress.Done, 1, 1, "导入成功！即将完成。"));
    }

    /// <summary>
    /// 从PCL2目录迁移所需文件，并返回需要网络下载的文件列表。
    /// 
    /// 意图解释：在当前设计中返回值不作使用，仅执行复制文件，不存在文件会在下载组件检查后下载
    /// </summary>
    private async Task<List<NdDowItem>> MigrateFilesFromPcl(List<NdDowItem> requiredFiles, string pclRoot, string olanRoot)
    {
        var filesToDownload = new List<NdDowItem>();
        int allFiles = requiredFiles.Count;
        int migratedCount = 0;
        _progress?.Report((DownProgress.Meta, allFiles, migratedCount, $"正在迁移文件"));
        foreach (var file in requiredFiles)
        {
            _token.ThrowIfCancellationRequested();

            // 将Olan的绝对路径转换为相对于.minecraft根目录的路径
            string relativePath = Path.GetRelativePath(olanRoot, file.path);
            string pclFilePath = Path.Combine(pclRoot, relativePath);

            if (File.Exists(pclFilePath))
            {
                // 本地文件存在，执行复制
                Directory.CreateDirectory(Path.GetDirectoryName(file.path));
                File.Copy(pclFilePath, file.path, true);
                migratedCount++;
            }
            else
            {
                // 本地文件不存在，加入下载列表
                filesToDownload.Add(file);
            }
        }
        return filesToDownload;
    }

    /// <summary>
    /// 迁移PCL2实例内容到OneLauncher实例目录。
    /// </summary>
    private async Task MigratePclInstanceContentAsync(string sourceInstancePath, string destinationInstancePath)
    {
        await Tools.CopyDirectoryAsync(sourceInstancePath, destinationInstancePath, _token); //
    }
    /// <summary>
    /// 从PCL2路径寻找版本文件以收集足够的信息完成导入
    /// </summary>
    private string FindPclVersionJson(string versionPath)
    {
        string expectedJsonName = $"{Path.GetFileName(versionPath)}.json";
        string primaryJsonPath = Path.Combine(versionPath, expectedJsonName);

        if (File.Exists(primaryJsonPath)) return primaryJsonPath;

        var anyJson = Directory.EnumerateFiles(versionPath, "*.json").FirstOrDefault();
        if (anyJson != null) return anyJson;

        throw new OlanException("导入失败", $"在 '{versionPath}' 中未找到任何 .json 配置文件。");
    }
}
