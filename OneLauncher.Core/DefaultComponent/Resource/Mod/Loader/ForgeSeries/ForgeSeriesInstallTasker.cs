using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Legacy;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Processors;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;

public delegate void ProcessorsOut(int all, int done, string message);

/// <summary>保留 Provider 入口，按包内协议准备和执行一次客户端安装。</summary>
internal sealed class ForgeSeriesInstallTasker
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Download _download;
    private readonly string _libraries;
    private readonly string _root;
    private ForgePreparedInstall? _prepared;
    public event ProcessorsOut? ProcessorsOutEvent;

    public ForgeSeriesInstallTasker(Download downloadTask, string librariesPath, string gameRootPath)
    {
        _download = downloadTask;
        _libraries = Path.GetFullPath(librariesPath);
        _root = Path.GetFullPath(gameRootPath);
    }

    public async Task<List<NdDowItem>> StartReadyAsync(string installerUrl, string modType, string versionId,
        Action<string>? reportStatus = null)
    {
        _prepared = null;
        reportStatus?.Invoke($"正在下载 {modType} 安装包，完成后解析依赖信息...");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var response = await _download.UnityClient.GetAsync(installerUrl, timeout.Token);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        reportStatus?.Invoke($"{modType} 安装包下载完成，正在解析安装协议及依赖...");
        try { _prepared = ForgeInstallerParser.Parse(bytes, modType, versionId, _libraries); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        { throw new OlanException($"{modType} 安装包无效", "归档或 JSON 结构不正确。", OlanExceptionAction.Error, ex); }
        reportStatus?.Invoke($"已确定 {_prepared.Protocol} 协议、{_prepared.Processors.Count} 个客户端处理器和 {_prepared.Downloads.Count} 个下载依赖。");
        return _prepared.Downloads;
    }

    public async Task RunProcessorsAsync(string mainJarPath, string javaPath, CancellationToken token)
    {
        var prepared = _prepared ?? throw new OlanException("安装顺序错误", "必须先准备 Forge 安装包。");
        // Forge 与 NeoForge 共享 libraries；同一游戏根的发布必须串行。
        var gate = InstallLocks.GetOrAdd(_root, _ => new(1, 1));
        await gate.WaitAsync(token);
        // Java 8 的 Windows 启动器会将 Unicode classpath 转为本地代码页。
        // 在系统临时目录运行处理器，避免游戏目录中的中文和深层路径使 JAR 无法加载。
        var work = Path.Combine(Path.GetTempPath(), "OneLauncherForge", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var stagedLibraries = Path.Combine(work, "libraries");
            var installer = Path.Combine(work, "installer.jar");
            await File.WriteAllBytesAsync(installer, prepared.Installer, token);
            var minecraftJar = Path.Combine(work, "minecraft.jar");
            await ForgeFileVerifier.RequireAsync(mainJarPath, [], 0, token);
            File.Copy(mainJarPath, minecraftJar);
            using (var archive = ZipFile.OpenRead(installer))
                foreach (var library in prepared.Libraries)
                {
                    token.ThrowIfCancellationRequested();
                    var staged = ForgePaths.Under(stagedLibraries, Path.GetRelativePath(_libraries, library.Download.path));
                    if (library.Generated)
                    {
                        if (await ForgeFileVerifier.ValidAsync(library.Download.path, library.Hashes, library.Download.size, token))
                            await ForgeFileVerifier.CopyAtomicAsync(library.Download.path, staged, token);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                    if (library.EmbeddedEntry != null)
                        archive.GetEntry(ForgePaths.EntryName(library.EmbeddedEntry))!.ExtractToFile(staged, true);
                    else
                    {
                        // 公共下载计划可能已过滤缓存，仍在此校验完整的 Forge 依赖。
                        if (!await ForgeFileVerifier.ValidAsync(library.Download.path, library.Hashes, library.Download.size, token))
                            await _download.DownloadFile(library.Download.url, staged, token);
                        else File.Copy(library.Download.path, staged);
                    }
                    await ForgeFileVerifier.RequireAsync(staged, library.Hashes, library.Download.size, token);
                }
            var tokens = new ForgeProcessorTokens(prepared, work, installer, minecraftJar);
            // 只复用有预期摘要的生成输出；无 outputs 的处理器仍照常执行。
            foreach (var name in new[] { "MC_SLIM", "MC_EXTRA", "PATCHED" })
                if (tokens.Has(name) && tokens.Has(name + "_SHA"))
                {
                    var staged = tokens.Key(name);
                    var cached = ForgePaths.Under(_libraries, Path.GetRelativePath(stagedLibraries, staged));
                    if (await ForgeFileVerifier.ValidAsync(cached, [tokens.Key(name + "_SHA")], 0, token))
                        await ForgeFileVerifier.CopyAtomicAsync(cached, staged, token);
                }
            var processors = prepared.Processors;
            for (var i = 0; i < processors.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var step = i + 1;
                void Report(string line) => ProcessorsOutEvent?.Invoke(processors.Count, step, line);
                Report($"正在执行 {processors[i].Jar}");
                if (await TryDownloadMappingsAsync(prepared, processors[i], tokens, mainJarPath, token))
                    Report("已按官方元数据下载并校验客户端 mappings。");
                else
                {
                    var processor = ForgeRealmsProcessor.Applies(processors[i])
                        ? await ForgeRealmsProcessor.PrepareAsync(processors[i], mainJarPath, work, _download, token)
                        : processors[i];
                    await ForgeProcessorRunner.RunAsync(processor, tokens, stagedLibraries, javaPath, work, Report, token);
                }
            }
            // 最后一步不一定声明 outputs，独立检查已知产物摘要。
            foreach (var name in new[] { "MC_SLIM", "MC_EXTRA", "PATCHED" })
                if (tokens.Has(name))
                    await ForgeFileVerifier.RequireAsync(tokens.Key(name), tokens.Has(name + "_SHA") ? [tokens.Key(name + "_SHA")] : [], 0, token);
            foreach (var library in prepared.Libraries)
                await ForgeFileVerifier.RequireAsync(ForgePaths.Under(stagedLibraries, Path.GetRelativePath(_libraries, library.Download.path)),
                    library.Hashes, library.Download.size, token);
            // 生成库先完成校验，再发布到既有 libraries。版本 JSON 最后提交。
            foreach (var file in Directory.EnumerateFiles(stagedLibraries, "*", SearchOption.AllDirectories))
                await ForgeFileVerifier.CopyAtomicAsync(file, ForgePaths.Under(_libraries, Path.GetRelativePath(stagedLibraries, file)), token);
            var target = ForgePaths.Under(_root, $"versions/{prepared.Minecraft}/version.{prepared.ModType.ToLowerInvariant()}.json");
            var candidate = Path.Combine(work, "version.json");
            await File.WriteAllTextAsync(candidate, prepared.VersionJson, new UTF8Encoding(false), token);
            await ForgeFileVerifier.CopyAtomicAsync(candidate, target, token);
            ProcessorsOutEvent?.Invoke(processors.Count, processors.Count, "加载器产物已校验，版本配置已提交。");
        }
        finally
        {
            // work 是本次生成的唯一临时目录，不删除共享库或原版游戏。
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"Forge 临时目录清理失败：{ex.Message}"); }
            catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine($"Forge 临时目录清理失败：{ex.Message}"); }
            finally { gate.Release(); }
        }
    }

    private async Task<bool> TryDownloadMappingsAsync(ForgePreparedInstall prepared, ForgeSeriesProcessor processor, ForgeProcessorTokens tokens,
        string originalJar, CancellationToken token)
    {
        if (!processor.Jar.StartsWith("net.minecraftforge:installertools:", StringComparison.Ordinal) ||
            !processor.Args.Contains("DOWNLOAD_MOJMAPS") || processor.Args.Contains("--sanitize")) return false;
        var args = processor.Args.Select(tokens.Resolve).ToList();
        string? Value(string flag)
        {
            var i = args.IndexOf(flag);
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }
        if (Value("--task") != "DOWNLOAD_MOJMAPS" || Value("--side") != "client" ||
            Value("--version") != prepared.Minecraft || Value("--output") != tokens.Key("MOJMAPS")) return false;
        var metadata = Path.Combine(Path.GetDirectoryName(originalJar)!, "version.json");
        if (!File.Exists(metadata)) return false;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadata, token));
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var id) || id.GetString() != prepared.Minecraft ||
            !root.TryGetProperty("downloads", out var downloads) || !downloads.TryGetProperty("client_mappings", out var mappings)) return false;
        var url = mappings.GetProperty("url").GetString()!;
        var hash = mappings.GetProperty("sha1").GetString()!;
        var size = mappings.GetProperty("size").GetInt64();
        var path = tokens.Key("MOJMAPS");
        await _download.DownloadFile(url, path, token);
        await ForgeFileVerifier.RequireAsync(path, [hash], size, token);
        return true;
    }
}
