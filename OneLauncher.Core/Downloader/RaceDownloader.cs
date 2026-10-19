using OneLauncher.Core.Downloader.DownloadMinecraftProviders.DownloadSources;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Core.Downloader;
internal class RaceDownloader
{
    private readonly HttpClient _httpClient;
    public RaceDownloader(HttpClient client) => _httpClient = client;

    internal Task RaceManyFilesAsync(
        IEnumerable<IEnumerable<NdDowItem>> basicItems, // 外部是单个文件，内部是单个文件的不同下载源
        int maxParallelism,
        IProgress<string> progress,
        CancellationToken token)
    {
        return RunRaceManyFilesAsync(basicItems, maxParallelism, progress, token);
    }

    private async Task RunRaceManyFilesAsync(
        IEnumerable<IEnumerable<NdDowItem>> basicItems,
        int maxParallelism,
        IProgress<string> progress,
        CancellationToken token)
    {
        try
        {
            await Parallel.ForEachAsync(basicItems,
                new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = token },
                async (item, ct) =>
                {
                    await RaceSingleFileAsync(item, ct);
                    if (!ct.IsCancellationRequested)
                        progress?.Report(item.FirstOrDefault().path);
                });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 并行下载中的取消由调用方继续处理，不转换为失败。
        }
    }

    public async Task RaceSingleFileAsync(
        IEnumerable<NdDowItem> item,
        CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        HttpResponseMessage? successfulResponse = null;

        // 同时启动所有源的下载任务
        Task<HttpResponseMessage>[] sourceTasks =
            item.Select(
                x =>
                _httpClient.GetAsync(x.url, HttpCompletionOption.ResponseHeadersRead, cts.Token))
            .ToArray();

        // 等待最先完成的任务并取消其他任务
        var winnerTask = await Task.WhenAny(sourceTasks);
        #region 正常情况
        try
        {
            HttpResponseMessage winnerResponse = await winnerTask;
            // 保留获胜响应的取消令牌，直到内容读取完成；否则会把获胜源的响应流一起取消。
            if (winnerResponse.IsSuccessStatusCode)
                successfulResponse = winnerResponse;
            else winnerResponse.Dispose();
        }
        catch (HttpRequestException)
        {
            // 如果失败了，等待回退
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 用户取消了下载，取消不是下载失败。
            return;
        }
        #endregion
        #region 回退机制
        if(successfulResponse == null)
        {
            // 找到其他非第一个的响应
            var remainingTasks = sourceTasks.Where(t => t != winnerTask);
            foreach (var task in remainingTasks)
            {
                try
                {
                    HttpResponseMessage response = await task;
                    if (response.IsSuccessStatusCode)
                    {
                        successfulResponse = response;
                        break; // 找到一个成功的响应就停止
                    }
                    else response.Dispose(); 
                }
                catch (HttpRequestException)
                {
                    // 忽略失败的请求
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        if(successfulResponse == null)
        {
            // 如果依旧没有成功的响应，抛出异常
            throw new OlanException("下载失败","所有下载源均无法访问头");
        }
        #endregion

        try
        {
            await ReadResponseAndWriteToFileAsync(
                successfulResponse,
                // 这里不关心下载地址是什么，只关心保存路径
                item.FirstOrDefault(),
                token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 内容流因用户取消而结束，交给上层按取消流程收尾。
            return;
        }
        finally
        {
            successfulResponse.Dispose();
            // 内容读取结束后再取消其余竞速请求，避免取消获胜响应的内容流。
            await cts.CancelAsync();
            try
            {
                await Task.WhenAll(sourceTasks);
            }
            catch (Exception) when (token.IsCancellationRequested || cts.IsCancellationRequested)
            {
                // 竞速中的其他源被取消是正常收尾，不应覆盖用户取消状态。
            }
        }
    }
    private async Task ReadResponseAndWriteToFileAsync(
        HttpResponseMessage response,
        NdDowItem main,
        CancellationToken cancelToken)
    {
        // 不包含SHA1校验
        using var pathLease = await DownloadFileOperations.AcquireAsync(main.path, cancelToken);
        string temporaryPath = DownloadFileOperations.CreateTemporaryPath(main.path);
        try
        {
            using var contentStream = await response.Content.ReadAsStreamAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(main.path)!);
            using (var fileStream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,8192,true))
                await contentStream.CopyToAsync(fileStream, cancelToken);

            File.Move(temporaryPath, main.path, true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // 清理失败不能覆盖原始下载异常。
            }
            catch (UnauthorizedAccessException)
            {
                // 清理失败不能覆盖原始下载异常。
            }

            throw;
        }
    }
}
