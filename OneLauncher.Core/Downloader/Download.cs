using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.ModService.Modrinth;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace OneLauncher.Core.Downloader;

public partial class Download : IDisposable
{
    /// <summary>
    /// 解压 ZIP 结构文件到指定目录
    /// </summary>
    /// <param ID="filePath">待解压的文件路径（例如 .docx 或其他 ZIP 结构文件）</param>
    /// <param ID="extractPath">解压到的目标目录</param>
    /// <exception cref="IOException">文件访问或解压失败</exception>
    /// <exception cref="InvalidDataException">文件不是有效的 ZIP 格式</exception>
    public static void ExtractFile(string filePath, string extractPath)
    {
            // 确保输出目录存在
            Directory.CreateDirectory(extractPath);

            // 打开 ZIP 文件
            using (ZipArchive archive = ZipFile.OpenRead(filePath))
            {
                // 遍历 ZIP 条目
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    // 确定解压路径
                    string destinationPath = Path.Combine(extractPath, entry.FullName);

                    // 确保目录存在
                    string destinationDir = Path.GetDirectoryName(destinationPath)
                    ?? throw new OlanException("无法解压",$"无法找到文件'{destinationPath}'对应的文件夹");
                    Directory.CreateDirectory(destinationDir);

                    // 仅处理文件（跳过目录）
                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        // 提取文件
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }
            }
    }
    public Download(HttpClient? tc = null)
    {
        unityClient = tc ?? new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = 32 
        })
        {
            Timeout = TimeSpan.FromSeconds(60) 
        };
        unityClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
    }
    public readonly HttpClient unityClient;

    /// <summary>
    /// 开始异步下载Mod（可选是否下载依赖项）
    /// </summary>
    /// <param Name="progress">进度回调：总字节数，已经下载的字节数，当前正在操作的文件名</param>
    /// <param Name="ModID">Mod ID （Modrinth）</param>
    /// <param Name="ModPath">Mods文件夹路径</param>
    /// <param Name="version">需要安装Mod的版本的版本号</param>
    /// <param Name="IsIncludeDependencies">是否下载依赖</param>
    /// <param Name="maxConcurrentDownloads">最大下载线程</param>
    /// <param Name="maxConcurrentSha1">最大Sha1校验线程</param>
    /// <param Name="IsSha1">是否校验Sha1</param>
    /// <returns></returns>
    public async Task StartDownloadMod(
        IProgress<(long AllSizes, long DownedSizes, string DowingFileName)> progress,
        string ModID,
        string ModPath,
        string version,
        ModEnum modType,
        bool IsIncludeDependencies = true,
        int maxConcurrentDownloads = 8,
        int maxConcurrentSha1 = 4,
        bool IsSha1 = true,
        CancellationToken? token = null
    )
    {
        CancellationToken cancellationToken = token ?? CancellationToken.None;

        var GetTask = new GetModrinth(ModID, version,modType, ModPath);
        await GetTask.Init();

        // 获取主 Mod 文件信息
        NdDowItem? mainMod = GetTask.GetDownloadInfos();
        if (!mainMod.HasValue)
            return;

        List<NdDowItem> allFiles = new List<NdDowItem> { (NdDowItem)mainMod };

        // 如果需要下载依赖项，则获取依赖项信息并添加到下载列表
        if (IsIncludeDependencies)
        {
            List<NdDowItem> dependencies = GetTask.GetDependenciesInfos();
            if (dependencies != null)
                allFiles.AddRange(dependencies);
        }

        // 过滤掉已经存在的文件
        List<NdDowItem> filesToDownload = CheckFilesExists(allFiles, cancellationToken);
        if (!filesToDownload.Any())
        {
            long totalExistingBytes = allFiles.Sum(item => (long)item.size);
            progress?.Report((totalExistingBytes, totalExistingBytes, "所有文件均已存在。"));
            if (IsSha1)
            {
                progress?.Report((totalExistingBytes, totalExistingBytes, "正在校验文件..."));
                await CheckAllSha1(allFiles, maxConcurrentSha1, cancellationToken);
            }
            progress?.Report((totalExistingBytes, totalExistingBytes, "处理完成！"));
            return;
        }

        long totalBytesToDownload = filesToDownload.Sum(item => (long)item.size);

        // --- 优化点 1: 移除 fileDownloadProgressMap, 使用一个 long 变量通过原子操作更新 ---
        long accumulatedDownloadedBytes = 0;

        // --- 优化点 2: 为进度报告节流（Throttling）准备变量 ---
        int lastReportTimestamp = 0;
        const int reportIntervalMs = 100; // 每 100ms 报告一次

        // 初始报告总大小和0已下载
        progress?.Report((totalBytesToDownload, 0, "开始下载Mod文件..."));

        var semaphore = new SemaphoreSlim(maxConcurrentDownloads);

        await Parallel.ForEachAsync(
            filesToDownload,
            new ParallelOptions { MaxDegreeOfParallelism = maxConcurrentDownloads, CancellationToken = cancellationToken },
            async (item, ct) =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    // ===== 主下载逻辑 =====
                    Directory.CreateDirectory(Path.GetDirectoryName(item.path)!);
                    using (var request = new HttpRequestMessage(HttpMethod.Get, item.url))
                    {
                        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true, MustRevalidate = true };
                        using (var response = await unityClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
                            {
                                using (var fileStream = new FileStream(item.path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true))
                                {
                                    var buffer = new byte[8192];
                                    int bytesRead;
                                    while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                                    {
                                        await fileStream.WriteAsync(buffer, 0, bytesRead, ct);

                                        // --- 优化点 3: 使用 Interlocked.Add 进行无锁、线程安全的累加 ---
                                        Interlocked.Add(ref accumulatedDownloadedBytes, bytesRead);

                                        // --- 优化点 4: 节流，控制进度报告频率 ---
                                        int currentTimestamp = Environment.TickCount;
                                        if (currentTimestamp - lastReportTimestamp > reportIntervalMs)
                                        {
                                            lastReportTimestamp = currentTimestamp;
                                            // 报告总进度。第一个参数应为总大小，以确保进度条最大值稳定
                                            progress?.Report((totalBytesToDownload, accumulatedDownloadedBytes, Path.GetFileName(item.path)));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (HttpRequestException) // 注意：只捕获 HttpRequestException 以进入重试
                {
                    // ===== 重试逻辑 (内联) =====
                    const int maxRetries = 3;
                    for (int attempt = 0; attempt < maxRetries; attempt++)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                            Directory.CreateDirectory(Path.GetDirectoryName(item.path)!);
                            using (var request = new HttpRequestMessage(HttpMethod.Get, item.url))
                            {
                                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true, MustRevalidate = true };
                                using (var response = await unityClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
                                {
                                    response.EnsureSuccessStatusCode();
                                    using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
                                    {
                                        using (var fileStream = new FileStream(item.path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true))
                                        {
                                            var buffer = new byte[8192];
                                            int bytesRead;
                                            while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                                            {
                                                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);

                                                // --- 优化点 3 (同样应用于重试逻辑): 使用 Interlocked.Add ---
                                                Interlocked.Add(ref accumulatedDownloadedBytes, bytesRead);

                                                // --- 优化点 4 (同样应用于重试逻辑): 节流 ---
                                                int currentTimestamp = Environment.TickCount;
                                                if (currentTimestamp - lastReportTimestamp > reportIntervalMs)
                                                {
                                                    lastReportTimestamp = currentTimestamp;
                                                    progress?.Report((totalBytesToDownload, accumulatedDownloadedBytes, Path.GetFileName(item.path)));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            break; // 重试成功，跳出 for 循环
                        }
                        catch (HttpRequestException ex2)
                        {
                            Debug.WriteLine($"重试下载失败: {ex2.Message}, URL: {item.url}");
                            if (attempt == maxRetries - 1) // 最后一次重试也失败
                            {
                                throw new OlanException("下载失败", "重试到达阈值抛出", OlanExceptionAction.Error, ex2);
                            }
                        }
                    }
                }
                catch (Exception ex) // 捕获其他未知异常
                {
                    Debug.WriteLine($"下载文件时发生意外错误: {ex.Message}, URL: {item.url}");
                    throw; // 重新抛出异常，让上层处理
                }
                finally
                {
                    semaphore.Release();
                }
            }
        );

        if (IsSha1)
        {
            progress?.Report((totalBytesToDownload, totalBytesToDownload, "正在校验文件..."));
            await CheckAllSha1(allFiles, maxConcurrentSha1, cancellationToken);
        }

        // 确保最后一次进度是100%
        progress?.Report((totalBytesToDownload, totalBytesToDownload, "下载完成！"));
    }

    public async Task DownloadFileBig(
        string url,
        string savePath,
        long? knownSize,
        int maxSegments,
        IProgress<(long Start, long End)>? segmentProgress = null,
        CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

        // 先确认服务端是否真的支持 Range。部分下载地址会忽略 Range 并返回
        // HTTP 200；若仍按分段偏移写入，会生成结构损坏的压缩包。
        using var probeRequest = new HttpRequestMessage(HttpMethod.Get, url);
        probeRequest.Headers.Range = new RangeHeaderValue(0, 0);
        probeRequest.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
            MustRevalidate = true
        };

        using var probeResponse = await unityClient.SendAsync(
            probeRequest,
            HttpCompletionOption.ResponseHeadersRead,
            token);
        probeResponse.EnsureSuccessStatusCode();

        if (probeResponse.StatusCode != HttpStatusCode.PartialContent)
        {
            await using var sourceStream = await probeResponse.Content.ReadAsStreamAsync(token);
            await using var targetStream = new FileStream(
                savePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await sourceStream.CopyToAsync(targetStream, token);

            long downloadedSize = targetStream.Length;
            if (downloadedSize == 0)
                throw new OlanException("下载失败", "服务端返回了空文件。");
            if (knownSize.HasValue && downloadedSize != knownSize.Value)
                throw new OlanException(
                    "下载失败",
                    $"下载文件大小不一致，预期 {knownSize.Value} 字节，实际 {downloadedSize} 字节。");

            segmentProgress?.Report((0, downloadedSize - 1));
            return;
        }

        ContentRangeHeaderValue? probeRange = probeResponse.Content.Headers.ContentRange;
        if (probeRange?.From != 0 || probeRange?.To != 0)
            throw new OlanException("下载失败", "服务端返回了无效的分段范围。");

        long fileSize = probeRange.Length
                        ?? knownSize
                        ?? throw new OlanException("下载失败", "无法确定文件大小。");
        probeResponse.Dispose();

        long interval = (fileSize + maxSegments - 1) / maxSegments;

        var segments = new List<(long Start, long End)>();
        for (long current = 0; current < fileSize; current += interval)
        {
            long end = Math.Min(current + interval - 1, fileSize - 1);
            segments.Add((current, end));
        }

        // 禁用FileStream内部缓冲，并使用WriteThrough模式
        // WriteThrough可以绕过操作系统缓存，对于随机写入可能提升性能
        await using var fileStream = new FileStream(
            savePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 0, // 禁用缓冲
            FileOptions.Asynchronous
        );
        fileStream.SetLength(fileSize);
        var fileHandle = fileStream.SafeFileHandle;

        // 下载
        await Parallel.ForEachAsync(
            segments,
            new ParallelOptions { MaxDegreeOfParallelism = maxSegments, CancellationToken = token },
            async (segment, ct) =>
            {
                // 租用内存池的缓冲区，顶级性能优化
                using var bufferOwner = MemoryPool<byte>.Shared.Rent(128 * 1024); // 128KB
                var buffer = bufferOwner.Memory;

                // 简单的重试逻辑
                const int maxRetries = 3;
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Range = new RangeHeaderValue(segment.Start, segment.End);

                        using var response = await unityClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                        response.EnsureSuccessStatusCode();

                        ContentRangeHeaderValue? responseRange = response.Content.Headers.ContentRange;
                        if (response.StatusCode != HttpStatusCode.PartialContent ||
                            responseRange?.From != segment.Start ||
                            responseRange?.To != segment.End)
                        {
                            throw new HttpRequestException(
                                $"服务端未返回请求的分段 {segment.Start}-{segment.End}。");
                        }

                        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);

                        long position = segment.Start;
                        int bytesRead;
                        while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
                        {
                            if (position + bytesRead > segment.End + 1)
                                throw new HttpRequestException(
                                    $"服务端返回的分段 {segment.Start}-{segment.End} 超出预期长度。");

                            // 使用 SafeFileHandle 和 RandomAccess 进行线程安全的并行写入
                            await RandomAccess.WriteAsync(fileHandle, buffer.Slice(0, bytesRead), position, ct);
                            position += bytesRead;
                        }

                        if (position != segment.End + 1)
                            throw new HttpRequestException(
                                $"服务端返回的分段 {segment.Start}-{segment.End} 长度不足。");

                        // 成功后才报告
                        segmentProgress?.Report((segment.Start, segment.End));
                        return; // 分片下载成功，退出重试循环
                    }
                    catch (HttpRequestException ex) when (attempt < maxRetries)
                    {
                        Debug.WriteLine(ex.Message);
                        // 发生异常都简单等待后重试
                        await Task.Delay(200 * attempt, ct);
                    }
                }
            }
        );
    }

    public Task DownloadListAsync(
        IProgress<(int completedFiles, string FilesName)> progress,
        List<NdDowItem> downloadNds,
        int maxConcurrentDownloads,
        CancellationToken token)
    {
        int completedFiles = 0;

        return Parallel.ForEachAsync(
            downloadNds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrentDownloads,
                CancellationToken = token
            },
            async (item, ct) =>
            {
                try
                {
                    // 原子递增已完成文件数
                    Interlocked.Increment(ref completedFiles);
                    // 报告进度
                    progress?.Report((completedFiles, item.url));
                    // 执行下载操作
                    await DownloadFile(item.url, item.path, ct);
                }
                catch (HttpRequestException ex)
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                        try
                        {
                            await DownloadFile(item.url, item.path, ct);
                            return; // 成功下载，退出重试循环
                        }
                        catch (HttpRequestException ex2)
                        {
                            Debug.WriteLine($"重试下载失败: {ex2.Message}, URL: {item.url}");
                            continue;
                        }
                    }
                    throw new OlanException("下载失败", "重试到达阈值抛出", OlanExceptionAction.Error, ex);
                }
            });
    }
    public async Task DownloadFile(string url,string savepath, CancellationToken? token = null)
    {
        if (string.IsNullOrEmpty(url))
            return;
        CancellationToken cancellationToken = token ?? CancellationToken.None;
        using (var response = await unityClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savepath)
                    ?? throw new OlanException("写入文件失败",$"无法找到文件'{savepath}'所对应的文件夹"));
                using (var fileStream = new FileStream(savepath, FileMode.Create, FileAccess.Write, FileShare.Write, bufferSize: 8192, useAsync: true))
                {
                    await httpStream.CopyToAsync(fileStream, 8192,cancellationToken);
                }
            }
        }
    }
    public async Task DownloadFileAndSha1(string url, string savepath,string sha1, CancellationToken token)
    {
        using (var response = await unityClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            using (var httpStream = await response.Content.ReadAsStreamAsync(token))
            {
                var directory = Path.GetDirectoryName(savepath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                using (var fileStream = new FileStream(savepath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, bufferSize: 8192, useAsync: true))
                {
                    await httpStream.CopyToAsync(fileStream, 8192, token);
                    fileStream.Position = 0;
                    using (var sha1Hash = SHA1.Create())
                    {
                        byte[] hash = await sha1Hash.ComputeHashAsync(fileStream,token);
                        string calculatedSha1 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        if (!string.Equals(calculatedSha1, sha1, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new OlanException(
                                "下载失败",
                                $"无法校验文件({savepath})Sha1，实际：{calculatedSha1}预期：{sha1}",
                                OlanExceptionAction.Warning);
                        }
                    }
                }
            }
        }
    }
    public List<NdDowItem> CheckFilesExists(List<NdDowItem> FDI, CancellationToken token)
    {
        List<NdDowItem> filesToDownload = new List<NdDowItem>(FDI.Count);
        foreach (var item in FDI)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(item.path))
                continue;
            filesToDownload.Add(item);
        }
        return filesToDownload;
    }
    public async Task CheckAllSha1(List<NdDowItem> FDI, int maxConcurrentSha1,CancellationToken token)
    {
        var semaphore = new SemaphoreSlim(maxConcurrentSha1);
        var sha1Tasks = new List<Task>(FDI.Count);
        foreach (var item in FDI)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(item.sha1))
                continue;
            if (!File.Exists(item.path))
                continue;
            sha1Tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                using (var stream = new FileStream(item.path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192, useAsync: true))
                using (var sha1Hash = SHA1.Create())
                {
                    byte[] hash = await sha1Hash.ComputeHashAsync(stream,token);
                    string calculatedSha1 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(calculatedSha1, item.sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new OlanException(
                            "下载失败",
                            $"无法校验文件({item.path})Sha1，实际：{calculatedSha1}预期：{item.sha1}",
                            OlanExceptionAction.Warning);
                    }
                }
                
                semaphore.Release();
            },token));
        }
        await Task.WhenAll(sha1Tasks);
    }
    public void Dispose() => unityClient.Dispose();
}
