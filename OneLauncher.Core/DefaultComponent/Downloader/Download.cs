using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;
using OneLauncher.Core.DefaultComponent.Resource.ModService.Modrinth;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace OneLauncher.Core.DefaultComponent.Downloader;

internal partial class Download : IDisposable
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
        ZipFile.ExtractToDirectory(filePath, extractPath, overwriteFiles: true);
    }
    public Download(HttpClient? tc = null)
    {
        UnityClient = tc ?? new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = 32 
        })
        {
            Timeout = TimeSpan.FromSeconds(60) 
        };
        UnityClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
    }
    public readonly HttpClient UnityClient;

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

        using var probeResponse = await UnityClient.SendAsync(
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

                        using var response = await UnityClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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
        return RunDownloadListAsync(progress, downloadNds, maxConcurrentDownloads, token);
    }

    private async Task RunDownloadListAsync(
        IProgress<(int completedFiles, string FilesName)> progress,
        List<NdDowItem> downloadNds,
        int maxConcurrentDownloads,
        CancellationToken token)
    {
        int completedFiles = 0;

        try
        {
            await Parallel.ForEachAsync(
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
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // 取消下载不进入重试，也不转换为失败。
                    }
                });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 并行下载已取消，保持任务的已取消状态。
        }
    }
    public async Task DownloadFile(string url,string savepath, CancellationToken? token = null)
    {
        if (string.IsNullOrEmpty(url))
            return;
        CancellationToken cancellationToken = token ?? CancellationToken.None;
        string temporaryPath = GetTemporaryDownloadPath(savepath);
        try
        {
            using (var response = await UnityClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(savepath)
                        ?? throw new OlanException("写入文件失败",$"无法找到文件'{savepath}'所对应的文件夹"));
                    using (var fileStream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true))
                    {
                        await httpStream.CopyToAsync(fileStream, 8192,cancellationToken);
                    }
                }
            }

            File.Move(temporaryPath, savepath, true);
        }
        catch
        {
            DeleteTemporaryDownload(temporaryPath);
            throw;
        }
    }

    private static string GetTemporaryDownloadPath(string savepath) => $"{savepath}.downloading";

    private static void DeleteTemporaryDownload(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // 清理失败不能覆盖原始下载异常。
        }
        catch (UnauthorizedAccessException)
        {
            // 清理失败不能覆盖原始下载异常。
        }
    }
    public async Task DownloadFileAndSha1(string url, string savepath,string sha1, CancellationToken token)
    {
        string temporaryPath = GetTemporaryDownloadPath(savepath);
        try
        {
            using (var response = await UnityClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                using (var httpStream = await response.Content.ReadAsStreamAsync(token))
                {
                    var directory = Path.GetDirectoryName(savepath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    using (var fileStream = new FileStream(temporaryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 8192, useAsync: true))
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

            File.Move(temporaryPath, savepath, true);
        }
        catch
        {
            DeleteTemporaryDownload(temporaryPath);
            throw;
        }
    }
    public List<NdDowItem> CheckFilesExists(List<NdDowItem> FDI, CancellationToken token)
    {
        List<NdDowItem> filesToDownload = new List<NdDowItem>(FDI.Count);
        foreach (var item in FDI)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(item.path))
            {
                // 取消下载可能留下旧版本的残缺目标文件；长度明显不符时强制重新下载。
                if (item.size <= 0 || new FileInfo(item.path).Length == item.size)
                    continue;

                DeleteTemporaryDownload(item.path);
            }
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
                            "安全验证异常",
                            $"无法校验文件({item.path})Sha1，实际：{calculatedSha1}预期：{item.sha1}{Environment.NewLine}您的电脑可能被黑客入侵了",
                            OlanExceptionAction.FatalError);
                    }
                }
                
                semaphore.Release();
            },token));
        }
        await Task.WhenAll(sha1Tasks);
    }
    public void Dispose() => UnityClient.Dispose();
}
