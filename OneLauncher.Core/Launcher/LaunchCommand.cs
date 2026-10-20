using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Launcher;
public sealed class LaunchCommand(IEnumerable<string> fileArgs, IEnumerable<string> commandArgs) : IDisposable
{
    private IEnumerable<string> commandArgs = commandArgs; // 必须在命令行的参数
    private IEnumerable<string> fileArgs = fileArgs; // 可以在命令行也可以在文件中的参数
    private string? tempFilePath = null;
    public void Dispose()
    {
        if (tempFilePath != null)
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
    }

    public async ValueTask<string> GetArguments()
    {
        string launchArg = string.Join(" ", fileArgs);
        string launchCommand = string.Join(" ", commandArgs);
        if (launchArg.Length > 32000) // 标准是8191的命令行长度上限，但这里调用的是CreateProcess API
        {
            Debug.WriteLine($"参数长度：{launchArg.Length}");
            // 引用类型保留异步方法中的文件状态，Dispose 才能清理实际使用的文件。
            tempFilePath ??= Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFilePath, launchArg
#if ONELAUNCHER_TARGET_WINDOWS
                .Replace("\\", @"\\") // Windows需要转义
#endif
                );
            return $"{launchCommand} @\"{tempFilePath}\"";
        }
        else
            return $"{launchCommand} {launchArg}";
    }
}
