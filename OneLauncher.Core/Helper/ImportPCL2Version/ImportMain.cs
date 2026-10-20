using OneLauncher.Core.Downloader;
using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using System.IO.Compression;
using System.Security.Cryptography;

namespace OneLauncher.Core.Helper.ImportPCL2Version;

/// <summary>迁移已经安装的 PCL 实例，保留其启动清单，不重新选择或安装加载器。</summary>
public class PCL2Importer
{
    private readonly IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>? _progress;
    private readonly CancellationToken _token;
    private readonly Download _downloader;
    private readonly string _gameRoot;
    private readonly Guid _userId;
    private readonly Func<GameData, Task> _register;

    public GameData? ImportedInstance { get; private set; }

    public PCL2Importer(IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress, CancellationToken? token = null)
        : this(Init.GameRootPath, Init.Download, Init.AccountManager.GetDefaultUser().UserID,
            Init.GameDataManager.AddGameDataAsync, progress, token ?? CancellationToken.None)
    {
    }

    // 隔离验证使用显式上下文，不能初始化或写入用户账号和实例配置。
    internal PCL2Importer(string gameRoot, Download downloader, Guid userId, Func<GameData, Task> register,
        IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>? progress = null,
        CancellationToken token = default)
    {
        _gameRoot = Path.GetFullPath(gameRoot);
        _downloader = downloader;
        _userId = userId;
        _register = register;
        _progress = progress;
        _token = token;
    }

    public async Task ImportAsync(string pclInstancePath)
    {
        ImportedInstance = null;
        _token.ThrowIfCancellationRequested();
        Report(DownProgress.Meta, 0, 0, "读取 PCL 启动配置");
        var metadata = await PclVersionResolver.ReadAsync(pclInstancePath, _token);
        var gameData = new GameData(metadata.Name, metadata.MinecraftVersion, metadata.ModLoader, _userId)
        {
            HasImportedLaunchManifest = true,
            ModLoaderVersion = metadata.LoaderVersion
        };
        string instanceRoot = PclImportPaths.Resolve(_gameRoot, "instance");
        string finalPath = PclImportPaths.Resolve(instanceRoot, gameData.InstanceId);
        string stagingPath = PclImportPaths.Resolve(instanceRoot, $".pcl-import-{Guid.NewGuid():N}");
        if (Directory.Exists(finalPath))
            throw new OlanException("导入失败", "目标实例目录已存在，请重新导入。");
        Directory.CreateDirectory(stagingPath);
        bool published = false;
        try
        {
            var version = new VersionInfomations(metadata.Manifest.ToJsonString(), _gameRoot);
            await EnsureResourceAsync(version.GetMainFile(), metadata.ClientJarPath, _token);
            var libraries = DownloadFileOperations.Deduplicate(version.GetLibraries().Concat(
                metadata.RuntimeResources.Select(resource => new NdDowItem("",
                    PclImportPaths.Resolve(_gameRoot, "libraries/" + resource.RelativePath), resource.Size, resource.Sha1))));
            Report(DownProgress.DownLibs, libraries.Count, 0, "迁移原实例依赖库");
            int completed = 0;
            foreach (var library in libraries)
            {
                string source = PclImportPaths.Resolve(metadata.MinecraftRoot, Path.GetRelativePath(_gameRoot, library.path));
                await EnsureResourceAsync(library, source, _token);
                Report(DownProgress.DownLibs, libraries.Count, ++completed, Path.GetFileName(library.path));
            }

            var index = version.GetAssets();
            await EnsureResourceAsync(index, PclImportPaths.Resolve(metadata.MinecraftRoot,
                Path.GetRelativePath(_gameRoot, index.path)), _token);
            var assets = DownloadFileOperations.Deduplicate(VersionAssetIndex.ParseAssetsIndex(
                await File.ReadAllTextAsync(index.path, _token), _gameRoot));
            int assetsDone = 0;
            await Parallel.ForEachAsync(assets, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = _token },
                async (asset, token) =>
                {
                    string source = PclImportPaths.Resolve(metadata.MinecraftRoot, Path.GetRelativePath(_gameRoot, asset.path));
                    await EnsureResourceAsync(asset, source, token);
                    int done = Interlocked.Increment(ref assetsDone);
                    Report(DownProgress.DownAssets, assets.Count, done, "迁移资源对象");
                });
            if (version.GetLoggingConfig() is { } logging)
            {
                string sourceLogging = PclImportPaths.Resolve(metadata.MinecraftRoot, $"assets/log_configs/{Path.GetFileName(logging.path)}");
                if (!File.Exists(sourceLogging)) sourceLogging = PclImportPaths.Resolve(metadata.InstancePath, Path.GetFileName(logging.path));
                await EnsureResourceAsync(logging, sourceLogging, _token);
            }

            Report(DownProgress.Meta, 0, 0, "迁移 Mods、配置和存档");
            await CopyContentAsync(metadata.InstancePath, stagingPath, metadata.VersionJsonPath, true);
            string natives = PclImportPaths.Resolve(stagingPath, "natives");
            foreach (string archivePath in version.NativesLibs.Distinct())
                ExtractNatives(archivePath, natives);

            // 仅修改副本的 id；完整库、加载器版本和参数仍来自原实例。
            await File.WriteAllTextAsync(Path.Combine(stagingPath, "launch.json"), metadata.Manifest.ToJsonString(), _token);
            _token.ThrowIfCancellationRequested();
            Directory.Move(stagingPath, finalPath);
            published = true;
            await _register(gameData);
            ImportedInstance = gameData;
            Report(DownProgress.Done, 1, 1, "导入完成");
        }
        catch
        {
            // 这两个目录都由本次新建，失败不回滚共享缓存、更不修改 PCL 原实例。
            string cleanupPath = published ? finalPath : stagingPath;
            PclImportPaths.RequireWithin(instanceRoot, cleanupPath);
            if (Directory.Exists(cleanupPath)) Directory.Delete(cleanupPath, true);
            throw;
        }
    }

    private async Task EnsureResourceAsync(NdDowItem item, string? source, CancellationToken token)
    {
        PclImportPaths.RequireWithin(_gameRoot, item.path);
        if (await IsValidAsync(item.path, item, token)) return;
        if (source != null) PclImportPaths.RequireRegular(source);
        if (source != null && await IsValidAsync(source, item, token))
        {
            using var lease = await DownloadFileOperations.AcquireAsync(item.path, token);
            if (await IsValidAsync(item.path, item, token)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(item.path)!);
            string temp = DownloadFileOperations.CreateTemporaryPath(item.path);
            try
            {
                await CopyFileAsync(source, temp, token);
                if (!await IsValidAsync(temp, item, token))
                    throw new OlanException("导入失败", $"复制后校验失败：{Path.GetFileName(item.path)}");
                File.Move(temp, item.path, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(item.url))
            throw new OlanException("导入失败", $"原实例缺少或损坏加载器生成文件：{Path.GetFileName(item.path)}。请先在 PCL 中修复该实例，再重新导入。");
        if (!Uri.TryCreate(item.url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new OlanException("导入失败", $"资源下载地址无效：{Path.GetFileName(item.path)}");
        if (!string.IsNullOrEmpty(item.sha1))
            await _downloader.DownloadFileAndSha1(item.url, item.path, item.sha1, token);
        else
            await _downloader.DownloadFile(item.url, item.path, token);
        if (!await IsValidAsync(item.path, item, token))
            throw new OlanException("导入失败", $"下载后校验失败：{Path.GetFileName(item.path)}");
    }

    private static async Task<bool> IsValidAsync(string path, NdDowItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return false;
        if (item.size > 0 && new FileInfo(path).Length != item.size) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (!string.IsNullOrEmpty(item.sha1) && !Convert.ToHexString(await SHA1.HashDataAsync(stream, token)).Equals(item.sha1, StringComparison.OrdinalIgnoreCase))
            return false;
        stream.Position = 0;
        if (Path.GetExtension(item.path).Equals(".jar", StringComparison.OrdinalIgnoreCase))
        {
            try { using var archive = new ZipArchive(stream, ZipArchiveMode.Read); return archive.Entries.Count > 0; }
            catch (InvalidDataException) { return false; }
        }
        return stream.Length > 0;
    }

    private async Task CopyContentAsync(string source, string destination, string versionJson, bool topLevel)
    {
        var files = new List<(string Source, string Target)>();
        PlanContent(source, destination, versionJson, topLevel, files);
        Report(DownProgress.DownAndInstModFiles, files.Count, 0, "迁移 Mods、配置和存档");
        int completed = 0;
        foreach (var file in files)
        {
            _token.ThrowIfCancellationRequested();
            await CopyFileAsync(file.Source, file.Target, _token);
            Report(DownProgress.DownAndInstModFiles, files.Count, ++completed,
                Path.GetRelativePath(destination, file.Target));
        }
    }

    private void PlanContent(string source, string destination, string versionJson, bool topLevel,
        List<(string Source, string Target)> files)
    {
        foreach (string file in Directory.EnumerateFiles(source))
        {
            _token.ThrowIfCancellationRequested();
            PclImportPaths.RequireRegular(file);
            string name = Path.GetFileName(file);
            if (topLevel && (file.Equals(versionJson, PclImportPaths.Comparison) ||
                name.Equals("launch.json", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(Path.GetFileName(source) + ".jar", StringComparison.OrdinalIgnoreCase))) continue;
            files.Add((file, PclImportPaths.Resolve(destination, name)));
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            _token.ThrowIfCancellationRequested();
            PclImportPaths.RequireRegular(directory);
            string name = Path.GetFileName(directory);
            if (topLevel && (name.Equals("PCL", StringComparison.OrdinalIgnoreCase) || name.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("natives", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-natives", StringComparison.OrdinalIgnoreCase))) continue;
            string target = PclImportPaths.Resolve(destination, name);
            Directory.CreateDirectory(target);
            PlanContent(directory, target, versionJson, false, files);
        }
    }

    private static async Task CopyFileAsync(string source, string target, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, token);
    }

    private void ExtractNatives(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            _token.ThrowIfCancellationRequested();
            string target = PclImportPaths.Resolve(destination, entry.FullName);
            if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private void Report(DownProgress stage, int all, int done, string message) => _progress?.Report((stage, all, done, message));
}
