using OneLauncher.Core.Launcher;
using System.Diagnostics;
using System.Text;

internal static class GameLaunchRegressionTests
{
    internal static async Task RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (string mode in new[] { "success", "fail" })
        {
            using var process = Child(mode);
            var lines = new List<string>();
            object sync = new();
            int closures = 0;
            process.OutputDataReceived += (_, value) => { if (value.Data != null) lock (sync) lines.Add(value.Data); };
            process.ErrorDataReceived += (_, value) => { if (value.Data != null) lock (sync) lines.Add(value.Data); };
            await GameLauncher.RunProcessAsync(process, code =>
            {
                closures++;
                Require(code == (mode == "fail" ? 1 : 0), "退出码丢失。");
                Require(lines.Contains("processor-final-error-中文"), "关闭事件早于 stderr EOF。");
            }, deadline.Token);
            Require(closures == 1 && process.HasExited, "未等待进程结束或关闭事件重复。");
        }
        Console.WriteLine("PASS: launch waits for exit / EOF precedes close / exact nonzero exit code");

        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
        using (var process = Child("wait"))
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.OutputDataReceived += (_, value) => { if (value.Data == "child-ready") ready.TrySetResult(); };
            int closures = 0;
            Task run = GameLauncher.RunProcessAsync(process, _ => closures++, cancel.Token);
            await ready.Task.WaitAsync(deadline.Token);
            Require(!run.IsCompleted, "Play 在子进程仍运行时提前完成。");
            cancel.Cancel();
            try { await run; throw new InvalidOperationException("取消未传播。"); }
            catch (OperationCanceledException) { }
            Require(run.IsCanceled && process.HasExited && closures == 1, "取消未终止进程或关闭事件重复。");
        }
        Console.WriteLine("PASS: canceled launch terminates process / waits for output / closes once");

        using (var process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(Path.GetTempPath(), "missing-java-" + Guid.NewGuid() + ".exe")) })
        {
            int closures = 0;
            try
            {
                await GameLauncher.RunProcessAsync(process, _ => closures++, deadline.Token);
                throw new InvalidOperationException("创建进程失败被吞掉。");
            }
            catch (System.ComponentModel.Win32Exception) { }
            Require(closures == 0, "未创建的进程触发了退出通知。");
        }
        string missing = LaunchDiagnostics.ReadOptionalLog(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log"));
        Require(missing.Contains("文件不存在", StringComparison.Ordinal), "缺失日志遮盖了原始错误。");
        string redacted = LaunchDiagnostics.Redact("\"--accessToken\" \"secret123\" --accessToken, other-secret, --version test", "secret123");
        Require(!redacted.Contains("secret123") && !redacted.Contains("other-secret") && redacted.Contains("--version test"), "启动凭证脱敏错误。");
        Console.WriteLine("PASS: failed process start / missing log fallback / credential redaction");

        string argumentFile;
        string longArgument = "-Dtest.value=" + new string('a', 33000);
        using (var command = new LaunchCommand([longArgument], []))
        {
            string arguments = await command.GetArguments();
            int prefix = arguments.IndexOf("@\"", StringComparison.Ordinal);
            Require(prefix >= 0 && arguments.EndsWith('"'), "长命令没有使用参数文件。");
            argumentFile = arguments[(prefix + 2)..^1];
            Require(await File.ReadAllTextAsync(argumentFile) == longArgument, "异步方法未保留参数文件内容。");
            Require(await command.GetArguments() == arguments, "重复获取参数泄漏了临时文件。");
            using var process = Child("success");
            await GameLauncher.RunProcessAsync(process, _ =>
                Require(File.Exists(argumentFile), "参数文件在进程结束前被删除。"), deadline.Token);
        }
        Require(!File.Exists(argumentFile), "进程退出后参数文件未清理。");
        Console.WriteLine("PASS: long launch argument file retained / reused / disposed after process exit");
        Console.WriteLine("PASS: all 4 game launch regression tests");
    }

    private static Process Child(string mode)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add(typeof(GameLaunchRegressionTests).Assembly.Location);
        info.ArgumentList.Add("--processor-test-child");
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add("game-final-output");
        return new Process { StartInfo = info };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
