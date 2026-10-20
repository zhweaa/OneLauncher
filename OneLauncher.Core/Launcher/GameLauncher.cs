using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Mod.ModLoader.forgeseries;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Core.Launcher;
internal sealed class GameLauncher : IDisposable
{
    public event Action<int>? GameClosedEvent;
    public event Action<string>? GameOutputEvent;
    private Process? _gameProcess;
    private string? _accessToken;
    public string GameDirectory { get; private set; } = "";
    private readonly string gameRootPath = Init.GameRootPath;
    private readonly AccountManager accountManager = Init.AccountManager;
    internal async Task BasicLaunch(
        GameData gameData,
        ServerInfo? serverInfo = null,
        bool useRootMode = false,
        IProgress<(string Current, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        #region 配置启动参数
        GameDirectory = useRootMode ? gameRootPath : gameData.InstancePath;
        if (gameData.HasImportedLaunchManifest && gameData.ModLoader == ModEnum.forge)
            ForgeRuntimeLayout.ValidateImportedFiles(gameRootPath, gameData.VersionId, gameData.ModLoaderVersion);
        // 先把令牌刷新了
        progress?.Report(("正在刷新登录账户...", 25));
        cancellationToken.ThrowIfCancellationRequested();
        UserModel user = Init.AccountManager.GetUser(gameData.DefaultUserModelID) ?? Init.AccountManager.GetDefaultUser();
        Task resh =
            user.IntelligentLogin(Init.MsalAuthenticator);
        progress?.Report(("正在读取游戏版本信息...", 35));
        LaunchCommandBuilder commandBuilder = (await LaunchCommandBuilder.CreateAsync(
                gameRootPath,
                gameData.VersionId,
                gameData.HasImportedLaunchManifest ? Path.Combine(gameData.InstancePath, "launch.json") : null
            ));
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(("正在配置游戏目录和启动参数...", 45));
        commandBuilder
            .SetLoginUser(accountManager.GetUser(gameData.DefaultUserModelID) ?? throw new OlanException("启动失败","找不到你想要启动的用户"))
            .WithServerInfo(serverInfo)
            .SetGamePath(GameDirectory)
            .SetModType(gameData.ModLoader);
        // 设置额外参数
        JvmArguments jvmArgsToUse = gameData.CustomJvmOptimizationArguments ?? Init.ConfigManger.Data.DefaultSettings.MinecraftJvmArguments;
        List<string> ojr = new(1);
        if (jvmArgsToUse.mode != OptimizationMode.None)
            ojr.AddRange(jvmArgsToUse.GetArguments(commandBuilder.versionInfo.GetJavaVersion(), gameData));
        if (gameData.ExtraJvmArguments != null)
            ojr.Add(gameData.ExtraJvmArguments);
        commandBuilder.WithExtraJvmArgs(ojr);
        if(gameData.CustomGameArguments != null)
            commandBuilder.WithExtraGameArgs([gameData.CustomGameArguments]);
        progress?.Report(("正在等待账户登录完成...", 55));
        await resh.WaitAsync(cancellationToken);
        _accessToken = user.AccessToken;
        #endregion
        // 配置并启动游戏进程
        try
        {
            
            progress?.Report(("正在解析并检查游戏依赖库...", 65));
            using (var launchCommand = await commandBuilder.BuildCommand())
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(("正在生成最终启动命令...", 75));
                string finalCommand = await launchCommand.GetArguments();
                var processInfo = new ProcessStartInfo
                {
                    FileName = commandBuilder.GetJavaPath(),
                    WorkingDirectory = GameDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    Arguments = finalCommand
                };
                await File.WriteAllTextAsync(Path.Combine(Init.BasePath, "LastLaunchArgs.bat"),
                    LaunchDiagnostics.Redact(finalCommand, _accessToken), cancellationToken);
                _gameProcess = new Process();
                _gameProcess.StartInfo = processInfo;
                _gameProcess.OutputDataReceived += OnOutputDataReceived;
                _gameProcess.ErrorDataReceived += OnErrorDataReceived;
                progress?.Report(("正在启动 Java 游戏进程...", 85));
                // 参数文件必须保留到 Java 退出；关闭事件等待 stdout/stderr 全部收尾。
                await RunProcessAsync(_gameProcess, code => GameClosedEvent?.Invoke(code), cancellationToken);
            }
        }
        catch(Win32Exception)
        {
            throw new OlanException("启动失败","无法找到Java运行环境，请确保已正确安装Java并在设置中配置了Java路径",OlanExceptionAction.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
    public Task Stop()
    {
        if (_gameProcess != null && !_gameProcess.HasExited)
        {
            _gameProcess.Kill(true); // 强制结束进程树
            return _gameProcess.WaitForExitAsync(CancellationToken.None); // 调用方可以选择等待游戏进程结束
        }
        else return Task.CompletedTask;
    }

    #region 事件处理
    internal static async Task RunProcessAsync(Process process, Action<int>? closed, CancellationToken token)
    {
        bool started = false;
        try
        {
            token.ThrowIfCancellationRequested();
            started = process.Start();
            if (!started) throw new InvalidOperationException("无法创建游戏进程。");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (started && !process.HasExited) process.Kill(entireProcessTree: true);
            if (started) await process.WaitForExitAsync();
            throw;
        }
        finally
        {
            if (started && process.HasExited)
            {
                // 输出流全部到达 EOF 后再通知关闭，失败诊断才能包含最后的 stderr。
                process.WaitForExit();
                closed?.Invoke(process.ExitCode);
            }
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;
        string text = LaunchDiagnostics.Redact(e.Data, _accessToken);
        Debug.WriteLine(text);
        GameOutputEvent?.Invoke($"[STDOUT] {text}{Environment.NewLine}");
    }
    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;
        string text = LaunchDiagnostics.Redact(e.Data, _accessToken);
        Debug.WriteLine(text);
        GameOutputEvent?.Invoke($"[ERROR] {text}{Environment.NewLine}");
    }

    public void Dispose()
    {
        _gameProcess?.Dispose();
    }
    #endregion
}
