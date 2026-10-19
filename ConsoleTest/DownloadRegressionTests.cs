using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using OneLauncher.Core.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

internal static class DownloadRegressionTests
{
    private static readonly byte[] Payload = [1, 2, 3, 4];
    private static readonly string Sha1 = Convert.ToHexString(SHA1.HashData(Payload)).ToLowerInvariant();

    internal static async Task RunAsync()
    {
        // 回归测试使用受控响应和独立临时目录，不初始化账号或真实游戏目录。
        string root = Path.Combine(Path.GetTempPath(), $"OneLauncher-download-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await DuplicateAndProgressAsync(root, deadline.Token);
            await SharedWritersAsync(root, deadline.Token);
            await CancelWaitingWriterAsync(root, deadline.Token);
            await CancelActiveWriterAsync(root, deadline.Token);
            await RetryAsync(root, deadline.Token);
            await FailedRetryAsync(root, deadline.Token);
            await FailedChecksumAsync(root, deadline.Token);
            CheckDeduplication(root);
            Console.WriteLine("PASS: all 8 download regression tests");
        }
        finally
        {
            // root 是本次测试创建的唯一目录，不包含用户数据。
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task DuplicateAndProgressAsync(string root, CancellationToken token)
    {
        string path = Path.Combine(root, "duplicate.jar");
        var first = Item(path);
        var alias = Item(Path.Combine(root, ".", "duplicate.jar"));
        var progress = new RecordedProgress();
        using var handler = new TestHandler(gateFirstRead: true);
        using var client = new HttpClient(handler);
        using var download = new Download(client);
        Task task = download.DownloadListAsync(progress, [first, alias, first], 24, token);
        await handler.ReadStarted.Task.WaitAsync(token);
        Require(handler.RequestCount == 1, "重复目标发出了多次 HTTP 请求。");
        Require(progress.Values.IsEmpty && !File.Exists(path), "文件尚未完成就报告了成功进度。");
        handler.AllowRead.TrySetResult(true);
        await task;
        Require(progress.Values.Count == 1 && progress.Values.Single().Completed == 1, "去重后的完成计数不正确。");
        await VerifyFileAsync(path, token);
        // 同一 URL 可以写入两个不同目标，内部回调必须返回真正完成的目标项。
        var callbacks = new ConcurrentBag<string>();
        string secondPath = Path.Combine(root, "same-url-second.jar");
        using var immediateDownload = new Download(new HttpClient(new TestHandler()));
        await immediateDownload.DownloadListAsync([Item(path), Item(secondPath)], 24, token,
            item => callbacks.Add(item.path));
        Require(callbacks.Count == 2 && callbacks.Contains(path) && callbacks.Contains(secondPath),
            "同 URL 的下载未按真实完成的目标项报告。");
        await VerifyFileAsync(secondPath, token);
        Console.WriteLine("PASS: duplicate targets / completion-only progress (24 workers)");
    }

    private static async Task SharedWritersAsync(string root, CancellationToken token)
    {
        string path = Path.Combine(root, "shared.jar");
        using var handler = new TestHandler(gateFirstRead: true);
        using var client = new HttpClient(handler);
        using var firstDownload = new Download(client);
        using var secondDownload = new Download(client);
        var race = new RaceDownloader(client);
        Task first = firstDownload.DownloadFile(Item(path).url, path, token);
        await handler.ReadStarted.Task.WaitAsync(token);
        Task second = secondDownload.DownloadFileAndSha1(Item(path).url, Path.Combine(root, ".", "shared.jar"), Sha1, token);
        Require(handler.RequestCount == 1, "不同 Download 实例未共享目标文件锁。");
        Task third = race.RaceSingleFileAsync([Item(path)], token);
        Require(!second.IsCompleted && !third.IsCompleted, "并发写入未等待已有文件下载。");
        handler.AllowRead.TrySetResult(true);
        await Task.WhenAll(first, second, third);
        await VerifyFileAsync(path, token);
        Console.WriteLine("PASS: Download / SHA1 / RaceDownloader shared target serialization");
    }

    private static async Task CancelWaitingWriterAsync(string root, CancellationToken token)
    {
        string path = Path.Combine(root, "queued.jar");
        using var handler = new TestHandler(gateFirstRead: true);
        using var client = new HttpClient(handler);
        using var download = new Download(client);
        Task first = download.DownloadFile(Item(path).url, path, token);
        await handler.ReadStarted.Task.WaitAsync(token);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task waiting = download.DownloadFile(Item(path).url, path, canceled.Token);
        canceled.Cancel();
        await ExpectCancellationAsync(waiting);
        Require(handler.RequestCount == 1 && Directory.GetFiles(root, "queued.jar.*.downloading").Length == 1,
            "等待者取消后影响了正在下载的文件。");
        handler.AllowRead.TrySetResult(true);
        await first;
        await download.DownloadFile(Item(path).url, path, token);
        await VerifyFileAsync(path, token);
        Console.WriteLine("PASS: canceled waiter preserves active download / releases lock");
    }

    private static async Task CancelActiveWriterAsync(string root, CancellationToken token)
    {
        string path = Path.Combine(root, "canceled.jar");
        using var handler = new TestHandler(gateFirstRead: true);
        using var client = new HttpClient(handler);
        using var download = new Download(client);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progress = new RecordedProgress();
        Task task = download.DownloadListAsync(progress, [Item(path)], 24, canceled.Token);
        await handler.ReadStarted.Task.WaitAsync(token);
        canceled.Cancel();
        await ExpectCancellationAsync(task);
        Require(progress.Values.IsEmpty && !File.Exists(path), "取消的任务被当作下载成功。");
        Require(Directory.GetFiles(root, "canceled.jar.*.downloading").Length == 0, "取消后临时文件未清理。");
        await download.DownloadFile(Item(path).url, path, token);
        await VerifyFileAsync(path, token);
        Console.WriteLine("PASS: active cancellation / temporary cleanup / retry after cancellation");
    }

    private static async Task RetryAsync(string root, CancellationToken token)
    {
        string path = Path.Combine(root, "retry.jar");
        using var handler = new TestHandler(failures: 1);
        using var client = new HttpClient(handler);
        using var download = new Download(client);
        var progress = new RecordedProgress();
        await download.DownloadListAsync(progress, [Item(path)], 24, token);
        Require(handler.RequestCount == 2 && progress.Values.Count == 1, "重试成功后未且仅报告一次完成。");
        await VerifyFileAsync(path, token);
        Console.WriteLine("PASS: HTTP retry success reports completion once");
    }

    private static async Task FailedRetryAsync(string root, CancellationToken token)
    {
        string path = Path.Combine(root, "failed.jar");
        using var handler = new TestHandler(failures: 4);
        using var client = new HttpClient(handler);
        using var download = new Download(client);
        var progress = new RecordedProgress();
        try
        {
            await download.DownloadListAsync(progress, [Item(path)], 24, token);
            throw new InvalidOperationException("网络重试耗尽后没有失败。");
        }
        catch (OlanException exception) when (exception.OriginalException is HttpRequestException)
        {
            Require(handler.RequestCount == 4 && progress.Values.IsEmpty && !File.Exists(path), "失败下载被计为完成或重试次数不正确。");
        }
        Console.WriteLine("PASS: exhausted HTTP retries fail without completion progress");
    }

    private static async Task FailedChecksumAsync(string root, CancellationToken token)
    {
        string path = Path.Combine(root, "checksum.jar");
        using var client = new HttpClient(new TestHandler());
        using var download = new Download(client);
        await download.DownloadFile(Item(path).url, path, token);
        try
        {
            await download.DownloadFileAndSha1(Item(path).url, path, new string('0', 40), token);
            throw new InvalidOperationException("错误 SHA1 被接受。");
        }
        catch (OlanException)
        {
            await VerifyFileAsync(path, token);
        }
        await download.DownloadFileAndSha1(Item(path).url, path, Sha1, token);
        await VerifyFileAsync(path, token);
        Console.WriteLine("PASS: checksum failure preserves target / cleans temporary / releases lock");
    }

    private static void CheckDeduplication(string root)
    {
        string path = Path.Combine(root, "missing.jar");
        using var client = new HttpClient(new TestHandler());
        using var download = new Download(client);
        List<NdDowItem> items = [Item(path), Item(Path.Combine(root, ".", "missing.jar"))];
        if (OperatingSystem.IsWindows())
            items.Add(Item(path.ToUpperInvariant()));
        Require(download.CheckFilesExists(items, CancellationToken.None).Count == 1, "下载计划未按规范化目标路径去重。");
        Console.WriteLine("PASS: normalized path deduplication (including Windows case aliases)");
    }

    private static NdDowItem Item(string path) => new("https://example.invalid/library.jar", path, Payload.Length, Sha1);

    private static async Task VerifyFileAsync(string path, CancellationToken token)
    {
        Require((await File.ReadAllBytesAsync(path, token)).SequenceEqual(Payload), "下载文件内容不正确。");
        Require(Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.*.downloading").Length == 0,
            "下载结束后残留临时文件。");
    }

    private static async Task ExpectCancellationAsync(Task task)
    {
        try
        {
            await task;
            throw new InvalidOperationException("取消的下载任务正常返回了成功。");
        }
        catch (OperationCanceledException)
        {
            Require(task.IsCanceled, "取消的下载任务没有保持 Canceled 状态。");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RecordedProgress : IProgress<(int completedFiles, string FilesName)>
    {
        internal readonly ConcurrentQueue<(int Completed, string Url)> Values = new();
        public void Report((int completedFiles, string FilesName) value) => Values.Enqueue(value);
    }

    private sealed class TestHandler(bool gateFirstRead = false, int failures = 0) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> AllowRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requests;
        internal int RequestCount => Volatile.Read(ref requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int count = Interlocked.Increment(ref requests);
            if (count <= failures)
                throw new HttpRequestException("受控网络失败");
            HttpContent content = count == 1 && gateFirstRead
                ? new StreamContent(new GatedStream(ReadStarted, AllowRead))
                : new ByteArrayContent(Payload);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class GatedStream(TaskCompletionSource<bool> started, TaskCompletionSource<bool> release) : MemoryStream(Payload, writable: false)
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            started.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            await base.CopyToAsync(destination, bufferSize, cancellationToken);
        }
    }
}
