using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using OneLauncher.Core.Global;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Processors;

internal static class ForgeProcessorRunner
{
    public static async Task RunAsync(ForgeSeriesProcessor processor, ForgeProcessorTokens tokens,
        string libraries, string java, string root, Action<string> report, CancellationToken token)
    {
        var outputs = processor.Outputs.Select(p => (Path: Path.GetFullPath(tokens.Resolve(p.Key)), Hash: tokens.Resolve(p.Value))).ToList();
        foreach (var output in outputs)
        {
            if (!output.Path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new OlanException("Forge 输出路径无效", output.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(output.Path)!);
        }
        if (outputs.Count > 0)
        {
            var valid = true;
            foreach (var output in outputs) valid &= await ForgeFileVerifier.ValidAsync(output.Path, [output.Hash], 0, token);
            if (valid) { report("处理器输出已校验，跳过。"); return; }
        }
        var jar = ForgePaths.Under(libraries, ForgePaths.RelativeMaven(processor.Jar));
        var classpath = processor.Classpath.Append(processor.Jar)
            .Select(c => ForgePaths.Under(libraries, ForgePaths.RelativeMaven(c))).Distinct().ToArray();
        foreach (var path in classpath)
            if (!File.Exists(path)) throw new OlanException("Forge 处理器缺少依赖", path);
        var args = processor.Args.Select(tokens.Resolve).ToList();
        // 无 outputs 的步骤也可能写新目录；只为实际位于工作区内的路径准备父目录。
        foreach (var arg in args)
            if (Path.IsPathFullyQualified(arg) && arg.StartsWith(root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                Directory.CreateDirectory(Path.GetDirectoryName(arg)!);
        var info = new ProcessStartInfo(java)
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        info.ArgumentList.Add("-cp");
        info.ArgumentList.Add(string.Join(Path.PathSeparator, classpath));
        info.ArgumentList.Add(MainClass(jar));
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new OlanException("Forge 处理器启动失败", processor.Jar);
        var diagnostics = new Queue<string>();
        void Capture(string line)
        {
            lock (diagnostics)
            {
                diagnostics.Enqueue(line);
                if (diagnostics.Count > 20) diagnostics.Dequeue();
            }
            report(line);
        }
        var outputPump = PumpAsync(process.StandardOutput, Capture);
        var errorPump = PumpAsync(process.StandardError, line => Capture("[stderr] " + line));
        try { await process.WaitForExitAsync(token); }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputPump, errorPump);
            throw;
        }
        await Task.WhenAll(outputPump, errorPump);
        if (process.ExitCode != 0)
            throw new OlanException("Forge 处理器失败", $"{processor.Jar} 退出码：{process.ExitCode}{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}");
        foreach (var output in outputs)
            await ForgeFileVerifier.RequireAsync(output.Path, [output.Hash], 0, token);
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> report)
    {
        while (await reader.ReadLineAsync() is { } line) report(line);
    }

    private static string MainClass(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("META-INF/MANIFEST.MF") ?? throw new OlanException("处理器损坏", $"缺少 MANIFEST：{path}");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var text = reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n ", "");
        foreach (var line in text.Split('\n'))
            if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase)) return line["Main-Class:".Length..].Trim();
        throw new OlanException("处理器损坏", $"未声明 Main-Class：{path}");
    }
}
