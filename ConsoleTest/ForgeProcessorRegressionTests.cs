using OneLauncher.Core.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Mod.ModLoader.forgeseries;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class ForgeProcessorRegressionTests
{
    private static readonly byte[] Mappings = Encoding.UTF8.GetBytes("# mappings\nexample -> a:\n");
    private static readonly string Sha1 = Convert.ToHexString(SHA1.HashData(Mappings)).ToLowerInvariant();

    internal static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), $"OneLauncher-processor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await MappingCacheAsync(root, deadline.Token);
            await MappingChecksumFailureAsync(root, deadline.Token);
            await MappingCancellationAsync(root, deadline.Token);
            await UnsupportedVariantAsync(root, deadline.Token);
            await ProcessOutputAsync(root, deadline.Token);
            await ProcessCancellationAsync(root, deadline.Token);
            Console.WriteLine("PASS: all 6 Forge processor regression tests");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task MappingCacheAsync(string root, CancellationToken token)
    {
        string metadata = await WriteMetadataAsync(root, "mapping-cache.json", Sha1, token);
        string target = Path.Combine(root, "maps with spaces.txt");
        using var handler = new MappingHandler();
        using var download = new Download(new HttpClient(handler));
        Require(await RunMappingsAsync(download, metadata, target, token), "未接管原始映射文件下载。");
        Require(handler.Requests == 1 && (await File.ReadAllBytesAsync(target, token)).SequenceEqual(Mappings), "下载映射错误。");
        Require(await RunMappingsAsync(download, metadata, target, token) && handler.Requests == 1, "有效缓存仍发起网络请求。");
        await File.WriteAllBytesAsync(target, new byte[Mappings.Length], token);
        await RunMappingsAsync(download, metadata, target, token);
        Require(handler.Requests == 2 && (await File.ReadAllBytesAsync(target, token)).SequenceEqual(Mappings), "同长度损坏缓存未重新下载。");
        Console.WriteLine("PASS: launcher mapping download / SHA1 cache / corrupted cache recovery");
    }

    private static async Task MappingChecksumFailureAsync(string root, CancellationToken token)
    {
        string metadata = await WriteMetadataAsync(root, "mapping-invalid.json", new string('0', 40), token);
        string target = Path.Combine(root, "invalid-mapping.txt");
        using var download = new Download(new HttpClient(new MappingHandler()));
        try
        {
            await RunMappingsAsync(download, metadata, target, token);
            throw new InvalidOperationException("错误 SHA1 的映射被当作成功。");
        }
        catch (OlanException)
        {
            Require(!File.Exists(target), "错误 SHA1 文件写入最终目标。");
        }
        Console.WriteLine("PASS: invalid mapping checksum fails / no fake processor success");
    }

    private static async Task MappingCancellationAsync(string root, CancellationToken token)
    {
        string metadata = await WriteMetadataAsync(root, "mapping-cancel.json", Sha1, token);
        using var handler = new MappingHandler(block: true);
        using var download = new Download(new HttpClient(handler));
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task task = RunMappingsAsync(download, metadata, Path.Combine(root, "cancel-mapping.txt"), canceled.Token);
        await handler.Started.Task.WaitAsync(token);
        canceled.Cancel();
        await ExpectCancellationAsync(task);
        Console.WriteLine("PASS: mapping cancellation propagates without success");
    }

    private static async Task UnsupportedVariantAsync(string root, CancellationToken token)
    {
        string metadata = await WriteMetadataAsync(root, "mapping-variant.json", Sha1, token);
        using var handler = new MappingHandler();
        using var download = new Download(new HttpClient(handler));
        var arguments = MappingArgs(Path.Combine(root, "sanitized.txt")).Append("--sanitize").ToList();
        Require(!await ForgeMappingsDownloader.TryDownloadAsync(download, "net.minecraftforge:installertools:1.4.1", arguments,
            metadata, null, token), "sanitize 变体不能按原始文件下载替代。");
        Require(handler.Requests == 0, "不支持的处理器变体发起了请求。");
        Console.WriteLine("PASS: unsupported mapping variants retain Java processor semantics");
    }

    private static async Task ProcessOutputAsync(string root, CancellationToken token)
    {
        string argument = "路径含 空格和中文";
        string successLog = Path.Combine(root, "success.log");
        await ForgeProcessorRunner.RunAsync(Child(root, "success", argument), successLog, 1, 2, null, token);
        string text = await File.ReadAllTextAsync(successLog, token);
        Require(text.Contains(argument, StringComparison.Ordinal) && text.Contains("processor-final-error-中文", StringComparison.Ordinal),
            "进程退出时未完整收集输出或参数空格/中文损坏。");
        string failureLog = Path.Combine(root, "failure.log");
        try
        {
            await ForgeProcessorRunner.RunAsync(Child(root, "fail", argument), failureLog, 2, 2, null, token);
            throw new InvalidOperationException("退出码 1 未被当作失败。");
        }
        catch (OlanException exception)
        {
            Require(exception.Message.Contains("processor-final-error-中文", StringComparison.Ordinal) &&
                exception.Message.Contains(failureLog, StringComparison.Ordinal), "异常未包含真实 stderr 和日志路径。");
        }
        Console.WriteLine("PASS: process EOF drain / failure diagnostics / exact Unicode and space arguments");
    }

    private static async Task ProcessCancellationAsync(string root, CancellationToken token)
    {
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task task = ForgeProcessorRunner.RunAsync(Child(root, "wait", ""), Path.Combine(root, "canceled-process.log"),
            1, 1, line => { if (line == "child-ready") ready.TrySetResult(true); }, canceled.Token);
        await ready.Task.WaitAsync(token);
        canceled.Cancel();
        await ExpectCancellationAsync(task);
        Console.WriteLine("PASS: canceled processor terminates / drains output / keeps Canceled state");
    }

    private static ProcessStartInfo Child(string root, string mode, string value)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add(typeof(ForgeProcessorRegressionTests).Assembly.Location);
        info.ArgumentList.Add("--processor-test-child");
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add(value);
        return info;
    }

    private static Task<bool> RunMappingsAsync(Download download, string metadata, string target, CancellationToken token) =>
        ForgeMappingsDownloader.TryDownloadAsync(download, "net.minecraftforge:installertools:1.4.1", MappingArgs(target), metadata, null, token);

    private static List<string> MappingArgs(string target) =>
        ["--task", "DOWNLOAD_MOJMAPS", "--version", "1.20.1", "--side", "client", "--output", target];

    private static async Task<string> WriteMetadataAsync(string root, string name, string sha1, CancellationToken token)
    {
        string path = Path.Combine(root, name);
        string json = JsonSerializer.Serialize(new { id = "1.20.1", downloads = new { client_mappings = new
            { url = "https://example.invalid/client.txt", size = Mappings.Length, sha1 } } });
        await File.WriteAllTextAsync(path, json, token);
        return path;
    }

    private static async Task ExpectCancellationAsync(Task task)
    {
        try
        {
            await task;
            throw new InvalidOperationException("取消操作被当作成功。");
        }
        catch (OperationCanceledException)
        {
            Require(task.IsCanceled, "取消的任务未处于 Canceled 状态。");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class MappingHandler(bool block = false) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requests;
        internal int Requests => Volatile.Read(ref requests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            Started.TrySetResult(true);
            if (block)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Mappings) };
        }
    }
}
