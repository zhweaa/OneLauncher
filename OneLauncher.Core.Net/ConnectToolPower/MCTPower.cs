using OneLauncher.Core.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Net.ConnectToolPower;

/// <summary>
/// 负责与 Minecraft Connect Tool 的核心文件交互
/// </summary>
public class MCTPower : IDisposable
{
    private const string CoreExecutableName = "main.exe";
    private const string CoreUrl = "https://api.mct.mczlf.loft.games/mainnew.exe";//"https://gitee.com/linfon18/minecraft-connect-tool-api/raw/master/mainnew.exe";
    private const string CoreMd5 = "08160296509deac13e7d12c8754de9ef";

    private readonly string coreDirectory;
    private readonly string coreFilePath;
    private Process? coreProcess;

    public event Action<string>? CoreLog;
    public event Action? ConnectionEstablished;

    private MCTPower(string coreDirectory,string coreFileName)
    {
        this.coreDirectory = coreDirectory;
        coreFilePath = coreFileName;
    }
    public static async Task<MCTPower> InitializationAsync()
    {
        string coreFileName = Path.Combine(Init.InstalledPath, CoreExecutableName);
        // 下载核心组件
        if (!File.Exists(coreFileName))
            await Init.Download.DownloadFile(CoreUrl, coreFileName);
        
        // 不管文件有没有，都跑一边校验
        string? currentMd5 = await Tools.GetFileMD5Async(coreFileName);
        if (currentMd5 == null)
            throw new OlanException("无法初始化联机模块", "在对核心程序校验时发生意外错误", OlanExceptionAction.Error);
        if (currentMd5 != CoreMd5)
        {
            File.Delete(coreFileName);
            throw new OlanException("无法初始化联机模块", $"【无法校验核心组件MD5】{Environment.NewLine}警告：您当前的网络环境可能不安全", OlanExceptionAction.FatalError);
        }
        return new MCTPower(Init.InstalledPath,coreFileName);
    }

    /// <summary>
    /// 启动核心进程。
    /// </summary>
    /// <param name="arguments">启动参数。</param>
    public void LaunchCore(string arguments)
    {
        if (coreProcess != null && !coreProcess.HasExited)
        {
            throw new InvalidOperationException("P2P核心已经在运行中。");
        }

        coreProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = coreFilePath,
                Arguments = arguments,
                WorkingDirectory = coreDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        // 他又让我改回第一版注释了，那就只能再次注释注释注释的注释了
        // 这里原本是// 他让我把注释删了，为了避免误人子弟，我这里把注释给注释掉
        // 这里原本是// 这里原来是// 解决作者写的屎山代码认配置文件不认命令行的问题
        var coreConfigPath = Path.Combine(coreDirectory, "config.json");
        if (File.Exists(coreConfigPath)) File.Delete(coreConfigPath);
        bool IsConnectionOk = false;
        coreProcess.OutputDataReceived += (s, e) => 
        {
            if (string.IsNullOrEmpty(e.Data))
                return;
            Debug.WriteLine(e.Data);
            CoreLog?.Invoke(e.Data);
            if ((e.Data.Contains("connection ok") || e.Data.Contains("handshakeC2C ok")) && !IsConnectionOk)
            {
                IsConnectionOk = true;
                ConnectionEstablished?.Invoke();
            }
        };
        coreProcess.ErrorDataReceived += (s, e) => 
        {
            if (string.IsNullOrEmpty(e.Data))
                return;
            Debug.WriteLine(e.Data);
            CoreLog?.Invoke(e.Data);
        };

        coreProcess.Start();
        coreProcess.BeginOutputReadLine();
        coreProcess.BeginErrorReadLine();
        //await coreProcess.WaitForExitAsync();
    }

    /// <summary>
    /// 停止核心进程。
    /// </summary>
    public void StopCore()
    {
        if (coreProcess != null && !coreProcess.HasExited)
        {
            try
            {
                coreProcess.Kill(true); 
                coreProcess.WaitForExit();
            }
            finally
            {
                coreProcess.Dispose();
                coreProcess = null;
            }
        }
    }

    public void Dispose() => StopCore();
}