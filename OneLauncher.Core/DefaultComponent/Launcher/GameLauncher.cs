using OneLauncher.Core.DefaultComponent.Launcher.Builders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Launcher;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Launcher;
internal sealed class GameLauncher : IDisposable
{
    public event Action<int>? GameClosedEvent;
    public event Action<string>? GameOutputEvent;
    private Process? _gameProcess;
    private readonly string gameRootPath = Init.GameRootPath;
    private readonly AccountManager accountManager = UnitedCore.Core.AccountManager;
    internal async Task BasicLaunch(
        GameData gameData,
        ServerInfo? serverInfo = null,
        bool useRootMode = false,
        IProgress<(string Current, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        #region 配置启动参数
        // 先把令牌刷新了
        progress?.Report(("正在刷新登录账户...", 25));
        cancellationToken.ThrowIfCancellationRequested();
        UserModel user = UnitedCore.Core.AccountManager.GetUser(gameData.DefaultUserModelID) ?? UnitedCore.Core.AccountManager.GetDefaultUser();
        Task resh =
            user.IntelligentLogin(UnitedCore.Core.MsalAuthenticator);
        progress?.Report(("正在读取游戏版本信息...", 35));
        LaunchCommandBuilder commandBuilder = (await LaunchCommandBuilder.CreateAsync(
                gameRootPath,
                gameData.VersionId
            ));
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(("正在配置游戏目录和启动参数...", 45));
        var launchGamePath = useRootMode ? gameRootPath : gameData.InstancePath;
        commandBuilder
            .SetLoginUser(accountManager.GetUser(gameData.DefaultUserModelID) ?? throw new OlanException("启动失败","找不到你想要启动的用户"))
            .WithServerInfo(serverInfo)
            .SetGamePath(launchGamePath)
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
                    WorkingDirectory = launchGamePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    Arguments = finalCommand
                };
                _ = File.WriteAllTextAsync(Path.Combine(Init.BasePath, "LastLaunchArgs.bat"), finalCommand);
                _gameProcess = new Process();
                _gameProcess.StartInfo = processInfo;
                _gameProcess.EnableRaisingEvents = true;
                _gameProcess.OutputDataReceived += OnOutputDataReceived;
                _gameProcess.ErrorDataReceived += OnErrorDataReceived;
                _gameProcess.Exited += OnGameProcessExited;
            }
            progress?.Report(("正在启动 Java 游戏进程...", 85));
            _gameProcess.Start();
            _gameProcess.BeginOutputReadLine();
            _gameProcess.BeginErrorReadLine();

            progress?.Report(("游戏进程已启动，正在等待游戏初始化...", 90));
            await _gameProcess.WaitForExitAsync(cancellationToken);
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
    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;
        Debug.WriteLine(e.Data);
        GameOutputEvent?.Invoke($"[STDOUT] {e.Data}{Environment.NewLine}");
        // 咱就是，其实没有什么真正好的判断游戏已经启动的方法
        //if (e.Data.Contains("Backend library: LWJGL version"))
        //    GameStartedEvent?.Invoke();
    }
    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;
        Debug.WriteLine(e.Data);
        GameOutputEvent?.Invoke($"[ERROR] {e.Data}{Environment.NewLine}");

        //if(e.Data.Contains("java.lang.ClassNotFoundException"))
        //    tcs.SetException(new OlanException("启动失败", "JVM无法找到主类，请确保你的游戏资源完整性", OlanExceptionAction.Error));
    }
    private void OnGameProcessExited(object? sender, EventArgs e)
    {
        GameClosedEvent?.Invoke(_gameProcess.ExitCode);
    }

    public void Dispose()
    {
        _gameProcess?.Dispose();
    }
    #endregion
}
