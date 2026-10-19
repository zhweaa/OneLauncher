using OneLauncher.Core.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;

namespace OneLauncher.Core.Mod.ModLoader.forgeseries
{
    public delegate void ProcessorsOut(int all, int done, string message);

    /// <summary>
    /// 通用安装器，负责处理所有基于 install_profile.json 的 Mod 加载器安装流程 (如 Forge, NeoForge)。
    /// </summary>
    internal class ForgeSeriesInstallTasker
    {
        private readonly Download _downloadTask;
        private readonly string librariesPath;
        private readonly string _gameRootPath; // 这个字段必须是 .minecraft 目录
        private ForgeSeriesInstallProfile _currentProfile;

        public event ProcessorsOut ProcessorsOutEvent;

        /// <summary>
        /// 构造一个通用的 Forge/NeoForge 安装器。
        /// </summary>
        /// <param name="downloadTask">用于下载的 Download 实例。</param>
        /// <param name="librariesPath">完整的 libraries 目录路径。</param>
        /// <param name="gameRootPath">完整的游戏根目录路径 (即 .minecraft 目录)。</param>
        public ForgeSeriesInstallTasker(
            Download downloadTask,
            string librariesPath,
            string gameRootPath)
        {
            _downloadTask = downloadTask;
            this.librariesPath = librariesPath;
            _gameRootPath = gameRootPath;
        }

        /// <summary>
        /// 准备阶段：下载安装器、解压并解析必要文件。
        /// </summary>
        /// <param name="installerUrl">安装程序的 URL。</param>
        /// <param name="modType">用于生成临时 JSON 文件名和错误信息，如 "Forge" 或 "NeoForge"。</param>
        /// <param name="versionId">当前安装的游戏版本ID，如 "1.20.4"。</param>
        /// <returns>包含所需下载库列表和 lzma 补丁临时路径的元组。</returns>
        public async Task<(List<NdDowItem> versionLibs, List<NdDowItem> installerLibs, string lzmaPath)> StartReadyAsync(string installerUrl, string modType, string versionId)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                using var response = await _downloadTask.UnityClient.GetAsync(installerUrl);
                response.EnsureSuccessStatusCode();
                await response.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

                var versionJsonEntry = archive.GetEntry("version.json") ?? throw new OlanException($"{modType} 安装失败", "安装包结构损坏：缺少 version.json 文件。", OlanExceptionAction.Error);
                var installProfileEntry = archive.GetEntry("install_profile.json") ?? throw new OlanException($"{modType} 安装失败", "安装包结构损坏：缺少 install_profile.json 文件。", OlanExceptionAction.Error);
                var dataClientLzmaEntry = archive.GetEntry("data/client.lzma") ?? throw new OlanException($"{modType} 安装失败", "安装包结构损坏：缺少 data/client.lzma 文件。", OlanExceptionAction.Error);

                using var vjs = versionJsonEntry.Open();
                using var ips = installProfileEntry.Open();
                var versionInfo = await JsonSerializer.DeserializeAsync(vjs, ForgeSeriesJsonContext.Default.ForgeSeriesVersionJson);
                _currentProfile = await JsonSerializer.DeserializeAsync(ips, ForgeSeriesJsonContext.Default.ForgeSeriesInstallProfile);

                if (_currentProfile == null || versionInfo == null)
                    throw new OlanException($"{modType} 安装失败", "无法解析安装配置文件，可能是安装包已损坏或版本不兼容。");

                var versionLibs = versionInfo.Libraries.Select(lib => new NdDowItem(lib.Downloads.Artifact.Url, Path.Combine(librariesPath, lib.Downloads.Artifact.Path.Replace('/', Path.DirectorySeparatorChar)), lib.Downloads.Artifact.Size, lib.Downloads.Artifact.Sha1)).ToList();
                var installerLibs = _currentProfile.Libraries.Select(lib => new NdDowItem(lib.Downloads.Artifact.Url, Path.Combine(librariesPath, lib.Downloads.Artifact.Path.Replace('/', Path.DirectorySeparatorChar)), lib.Downloads.Artifact.Size, lib.Downloads.Artifact.Sha1)).ToList();

                var clientLzmaTempPath = Path.GetTempFileName();
                var tempVersionJsonPath = Path.Combine(_gameRootPath, "versions", versionId, $"version.{modType.ToLower()}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(tempVersionJsonPath));

                using (var vjsr = versionJsonEntry.Open())
                using (var fs = new FileStream(tempVersionJsonPath, FileMode.Create, FileAccess.Write))
                    await vjsr.CopyToAsync(fs);

                using (var dclsr = dataClientLzmaEntry.Open())
                using (var fs = new FileStream(clientLzmaTempPath, FileMode.Create, FileAccess.Write))
                    await dclsr.CopyToAsync(fs);

                return (versionLibs, installerLibs, clientLzmaTempPath);
            }
            catch (HttpRequestException ex) { throw new OlanException($"{modType} 安装失败", "下载安装器时发生网络错误，请检查网络连接。", OlanExceptionAction.Error, ex); }
            catch (JsonException ex) { throw new OlanException($"{modType} 安装失败", "解析安装配置文件时出错，可能是安装包已损坏或版本不兼容。", OlanExceptionAction.Error, ex); }
            catch (IOException ex) { throw new OlanException($"{modType} 安装失败", "读写临时文件时发生 IO 错误，请检查磁盘空间和权限。", OlanExceptionAction.Error, ex); }
        }

        /// <summary>
        /// 执行阶段：按顺序运行所有客户端安装处理器。
        /// </summary>
        public async Task RunProcessorsAsync(string mainJarPath, string javaPath, string clientLzmaPath, CancellationToken token,bool isForge = false)
        {
            if (_currentProfile == null)
                throw new OlanException("安装逻辑错误", "必须先调用 StartReadyAsync 来准备安装配置文件。", OlanExceptionAction.FatalError);

            var placeholderDict = BuildPlaceholderDictionary(_currentProfile, librariesPath, _gameRootPath, mainJarPath, clientLzmaPath);
            var clientProcessors = _currentProfile.Processors.Where(p => p.Sides == null || p.Sides.Contains("client")).ToList();
            int alls = clientProcessors.Count;
            int dones = 0;

            ProcessorsOutEvent?.Invoke(alls, 0, "正在准备安装处理器...");
            // 复制MC主文件
            if (isForge)
            {
                if (placeholderDict.TryGetValue("MC_SRG", out string srgPath))
                {
                    try
                    {
                        ProcessorsOutEvent?.Invoke(alls, 0, "正在为 Forge 准备处理器工作文件...");
                        Directory.CreateDirectory(Path.GetDirectoryName(srgPath));
                        File.Copy(mainJarPath, srgPath, true);
                    }
                    catch (Exception ex)
                    {
                        throw new OlanException("文件准备失败", $"为 Forge 创建工作副本 ({Path.GetFileName(srgPath)}) 时出错。", OlanExceptionAction.Error, ex);
                    }
                }
            }

            foreach (var procDef in clientProcessors)
            {
                token.ThrowIfCancellationRequested();
                dones++;
                ProcessorsOutEvent?.Invoke(alls, dones, $"正在准备处理器 {dones}/{alls}: {procDef.Jar}");

                CreateOutputDirectories(procDef.Args, placeholderDict, librariesPath, alls, dones);

                var mainJar = Tools.MavenToPath(librariesPath, procDef.Jar);
                var mainClass = await GetMainClassFromJar(mainJar);
                var cpArgs = BuildClasspathArgument(procDef.Classpath, librariesPath, mainJar);
                var stdArgs = BuildProcessorArguments(procDef.Args, placeholderDict, librariesPath, alls, dones);

                string logDirectory = Path.Combine(_gameRootPath, "logs", "installer");
                Directory.CreateDirectory(logDirectory);
                string logPath = Path.Combine(logDirectory, $"processor-{dones}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
                Action<string> report = message => ProcessorsOutEvent?.Invoke(alls, dones, message);
                if (await ForgeMappingsDownloader.TryDownloadAsync(_downloadTask, procDef.Jar, stdArgs,
                    Path.Combine(Path.GetDirectoryName(mainJarPath)!, "version.json"), report, token))
                {
                    await File.WriteAllTextAsync(logPath, "Mojang 映射由启动器下载/缓存，大小和 SHA1 校验通过。", token);
                    continue;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _gameRootPath,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                startInfo.ArgumentList.Add("-cp");
                startInfo.ArgumentList.Add(cpArgs);
                startInfo.ArgumentList.Add(mainClass);
                foreach (string arg in stdArgs)
                    startInfo.ArgumentList.Add(arg);

                ProcessorsOutEvent?.Invoke(alls, dones, $"正在运行处理器 {dones}/{alls}...");
                await ForgeProcessorRunner.RunAsync(startInfo, logPath, dones, alls, report, token);
            }
            ProcessorsOutEvent?.Invoke(alls, alls, "所有安装处理器已成功运行！");
        }

        #region Private Helper Methods

        private void CreateOutputDirectories(List<string> args, Dictionary<string, string> placeholders, string librariesPath, int all, int done)
        {
            foreach (var argTemplate in args)
            {
                string resolvedPath = null;
                if (argTemplate.StartsWith("{") && argTemplate.EndsWith("}"))
                {
                    if (placeholders.TryGetValue(argTemplate.Trim('{', '}'), out var p)) resolvedPath = p;
                }
                else if (argTemplate.StartsWith("["))
                {
                    resolvedPath = Tools.MavenToPath(librariesPath, argTemplate);
                }

                if (resolvedPath != null)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(resolvedPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    }
                    catch (Exception ex)
                    {
                        ProcessorsOutEvent?.Invoke(all, done, $"[警告] 为参数 {argTemplate} 创建目录时出错: {ex.Message}");
                    }
                }
            }
        }

        private async Task<string> GetMainClassFromJar(string jarPath)
        {
            try
            {
                using var fs = new FileStream(jarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
                var manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");
                if (manifestEntry != null)
                {
                    using var reader = new StreamReader(manifestEntry.Open());
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (line.StartsWith("Main-Class: "))
                        {
                            var mainClass = line.Substring("Main-Class: ".Length).Trim();
                            if (!string.IsNullOrEmpty(mainClass)) return mainClass;
                        }
                    }
                }
            }
            catch (IOException ex) { throw new OlanException("文件读取错误", $"无法读取处理器 JAR 文件。文件可能已损坏或被占用。", OlanExceptionAction.Error, ex); }

            throw new OlanException("安装配置错误", $"在处理器的 MANIFEST.MF 文件中未找到有效的 'Main-Class' 定义。", OlanExceptionAction.Error);
        }

        private string BuildClasspathArgument(List<string> classpath, string librariesPath,string mainPath)
        {
            var cpBuilder = new StringBuilder();
            char sep = Path.PathSeparator;

            foreach (var cpEntry in classpath)
            {
                cpBuilder.Append(Tools.MavenToPath(librariesPath, cpEntry));
                cpBuilder.Append(sep);
            }
            return $"{cpBuilder}{mainPath}";
        }

        private List<string> BuildProcessorArguments(List<string> argsTemplate, Dictionary<string, string> placeholders, string librariesPath, int all, int done)
        {
            var arguments = new List<string>();
            foreach (var arg in argsTemplate)
            {
                if (arg.StartsWith("{") && arg.EndsWith("}"))
                {
                    if (placeholders.TryGetValue(arg.Trim('{', '}'), out var value)) arguments.Add(value);
                    else throw new OlanException("安装配置错误", $"处理器 {done}/{all} 的占位符 {arg} 无法解析。");
                }
                else if (arg.StartsWith("["))
                {
                    arguments.Add(Tools.MavenToPath(librariesPath, arg));
                }
                else
                {
                    arguments.Add(arg);
                }
            }
            return arguments;
        }

        private Dictionary<string, string> BuildPlaceholderDictionary(ForgeSeriesInstallProfile profile, string librariesPath, string gameRootPath, string minecraftJarPath, string clientLzmaPath)
        {
            var placeholders = new Dictionary<string, string>
            {
                { "SIDE", "client" }, { "MINECRAFT_JAR", minecraftJarPath },
                { "LIBRARY_DIR", librariesPath }, { "ROOT", gameRootPath },
            };

            if (profile.Data?.Placeholders == null) return placeholders;

            foreach (var entry in profile.Data.Placeholders)
            {
                if (entry.Value is not JsonElement element) continue;
                string rawValue = "";
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("client", out var clientElement))
                    rawValue = clientElement.GetString() ?? "";
                else if (element.ValueKind == JsonValueKind.String)
                    rawValue = element.GetString() ?? "";

                if (string.IsNullOrEmpty(rawValue)) continue;

                string processedValue;
                if (rawValue.StartsWith("[") && rawValue.EndsWith("]"))
                    processedValue = Tools.MavenToPath(librariesPath, rawValue);
                else if (rawValue.StartsWith("'") && rawValue.EndsWith("'"))
                    processedValue = rawValue.Trim('\'');
                else
                    processedValue = rawValue;

                placeholders[entry.Key] = processedValue;
            }
            if (placeholders.ContainsKey("BINPATCH"))
                placeholders["BINPATCH"] = clientLzmaPath;

            return placeholders;
        }

        #endregion
    }
}
