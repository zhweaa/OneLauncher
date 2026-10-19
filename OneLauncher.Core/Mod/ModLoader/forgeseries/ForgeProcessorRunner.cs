using OneLauncher.Core.Global;
using System.Diagnostics;
using System.Text;

namespace OneLauncher.Core.Mod.ModLoader.forgeseries;

internal static class ForgeProcessorRunner
{
    internal static async Task RunAsync(ProcessStartInfo startInfo, string logPath,
        int current, int total, Action<string>? output, CancellationToken token)
    {
        using var process = new Process { StartInfo = startInfo };
        using var log = new StreamWriter(logPath, append: false, new UTF8Encoding(false));
        var recentErrors = new Queue<string>();
        var recentOutput = new Queue<string>();
        object syncRoot = new();
        Task stdout = Task.CompletedTask;
        Task stderr = Task.CompletedTask;
        bool started = false;
        try
        {
            log.WriteLine($"处理器 {current}/{total}");
            log.WriteLine($"Java: {startInfo.FileName}");
            log.WriteLine($"工作目录: {startInfo.WorkingDirectory}");
            log.WriteLine($"参数: {string.Join(" ", startInfo.ArgumentList)}");
            log.Flush();
            started = process.Start();
            if (!started)
                throw new InvalidOperationException("未能启动安装处理器。");
            stdout = ReadOutputAsync(process.StandardOutput, "stdout", recentOutput);
            stderr = ReadOutputAsync(process.StandardError, "stderr", recentErrors);
            await process.WaitForExitAsync(token);
            // 进程退出不代表输出事件已全部处理；读取到 EOF 后再组织错误信息。
            await Task.WhenAll(stdout, stderr);
            log.WriteLine($"退出代码: {process.ExitCode}");
            log.Flush();
            if (process.ExitCode != 0)
            {
                string details = string.Join(Environment.NewLine, recentErrors.Count > 0 ? recentErrors : recentOutput);
                throw new OlanException("安装处理器失败",
                    $"处理器 {current}/{total} 执行失败，退出代码为: {process.ExitCode}。{Environment.NewLine}" +
                    $"日志：{logPath}{Environment.NewLine}{details}", OlanExceptionAction.Error);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (started && !process.HasExited)
                process.Kill(entireProcessTree: true);
            if (started)
            {
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
            }
            log.WriteLine("处理器已取消。");
            throw;
        }
        catch (Exception ex) when (ex is not OlanException)
        {
            throw new OlanException("处理器调用失败",
                $"启动或等待处理器 {current}/{total} 时出错：{ex.Message}{Environment.NewLine}日志：{logPath}",
                OlanExceptionAction.Error, ex);
        }

        async Task ReadOutputAsync(StreamReader reader, string source, Queue<string> recent)
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lock (syncRoot)
                {
                    log.WriteLine($"[{source}] {line}");
                    log.Flush();
                    recent.Enqueue(line.Length > 500 ? line[..500] : line);
                    while (recent.Count > 12)
                        recent.Dequeue();
                }
                output?.Invoke(line);
            }
        }
    }
}
