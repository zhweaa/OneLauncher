using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Core.Launcher;
public class GameLauncher : IGameLauncher, IDisposable
{
    public event Action? GameStartedEvent;
    public event Action<int>? GameClosedEvent;
    public event Action<string>? GameOutputEvent;
    public CancellationToken CancellationToken = CancellationToken.None; // 外部可以设置

    private Process? _gameProcess;
    private readonly TaskCompletionSource<Task> tcs = new();

    private readonly string gameRootPath = Init.GameRootPath;
    private readonly AccountManager accountManager = Init.AccountManager;
    private async Task<Task> BasicLaunch(GameData gameData,ServerInfo? serverInfo = null,bool useRootMode = false)
    {
        #region 准备工作
        // 先把令牌刷新了
        UserModel user = Init.AccountManager.GetUser(gameData.DefaultUserModelID) ?? Init.AccountManager.GetDefaultUser();
        Task resh =
            user.IntelligentLogin(Init.MsalAuthenticator);
        #region 外置登入补丁
        var libPath = Path.Combine(Init.InstalledPath, "authlib.jar");
        if(!File.Exists(libPath))
            // 如果不存在authlib.jar，则尝试下载
            await Init.Download
                .DownloadFile(
                "https://authlib-injector.yushi.moe/artifact/53/authlib-injector-1.2.5.jar",
                libPath
                );
        
        #endregion
        // 帮用户设置一下语言
        var optionsPath = Path.Combine(gameData.InstancePath, "options.txt");
        if (!File.Exists(optionsPath))
            await File.WriteAllTextAsync(optionsPath, "lang:zh_CN");
        LaunchCommandBuilder commandBuilder = (await LaunchCommandBuilder.CreateAsync(
                gameRootPath,
                gameData.VersionId
            ));
        commandBuilder
            .SetLoginUser(accountManager.GetUser(gameData.DefaultUserModelID) ?? throw new OlanException("启动失败","找不到你想要启动的用户"))
            .WithServerInfo(serverInfo)
            .SetGamePath(useRootMode ? gameRootPath : gameData.InstancePath)
            .SetModType(gameData.ModLoader);
        // 设置额外参数
        JvmArguments jvmArgsToUse = gameData.CustomJvmOptimizationArguments ?? Init.ConfigManger.Data.OlanSettings.MinecraftJvmArguments;
        List<string> ojr = new(1);
        if (jvmArgsToUse.mode != OptimizationMode.None)
            ojr.AddRange(jvmArgsToUse.GetArguments(commandBuilder.versionInfo.GetJavaVersion(), gameData));
        if (gameData.ExtraJvmArguments != null)
            ojr.Add(gameData.ExtraJvmArguments);
        commandBuilder.WithExtraJvmArgs(ojr);
        if(gameData.CustomGameArguments != null)
            commandBuilder.WithExtraGameArgs([gameData.CustomGameArguments]);
        await resh;
        #endregion
        // 配置并启动游戏进程
        try
        {
            using (var launchCommand = await commandBuilder.BuildCommand())
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = commandBuilder.GetJavaPath(),
                    WorkingDirectory = Init.GameRootPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    Arguments = await launchCommand.GetArguments()
                };
                _gameProcess = new Process();
                _gameProcess.StartInfo = processInfo;
                _gameProcess.EnableRaisingEvents = true;
                _gameProcess.OutputDataReceived += OnOutputDataReceived;
                _gameProcess.ErrorDataReceived += OnErrorDataReceived;
                _gameProcess.Exited += OnGameProcessExited;
            }
            _gameProcess.Start();
            _gameProcess.BeginOutputReadLine();
            _gameProcess.BeginErrorReadLine();

            tcs.SetResult(_gameProcess.WaitForExitAsync(CancellationToken)); 
            return tcs.Task;
        }
        catch(Win32Exception)
        {
            throw new OlanException("启动失败","无法找到Java运行环境，请确保已正确安装Java并在设置中配置了Java路径",OlanExceptionAction.Error);
        }
        catch (OperationCanceledException)
        {
            return Stop();
        }
    }
    public Task Play(string InstanceID)
    {
        GameData? gameData = Init.GameDataManager.GetInstanceFromId(InstanceID);
        if (gameData == null)
            throw new OlanException("启动失败",$"没有找到匹配的且已安装的实例{Environment.NewLine}请检查你的输入{InstanceID}是否正确");
        return BasicLaunch(gameData);
    }
    public async Task Play(GameData gameData, ServerInfo? serverInfo = null)
        => await await BasicLaunch(gameData, serverInfo);
    
    public async Task Play(UserVersion userVersion, ServerInfo? serverInfo = null, bool useRootMode = false)
    {
        GameData finded = await Init.GameDataManager.GetOrCreateInstanceAsync(userVersion);
        await await BasicLaunch(finded, serverInfo, useRootMode); 
    }
    public Task Stop()
    {
        if (_gameProcess != null && !_gameProcess.HasExited)
        {
            _gameProcess.Kill(true); // 强制结束进程树
            return _gameProcess.WaitForExitAsync(CancellationToken); // 调用方可以选择等待游戏进程结束
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
        if (e.Data.Contains("Backend library: LWJGL version"))
            GameStartedEvent?.Invoke();
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
        //if(_gameProcess?.ExitCode != 0)
        //    tcs.SetException(new OlanException("游戏异常退出", $"检测到游戏异常退出，代码：{_gameProcess.ExitCode}{Environment.NewLine}建议尝试以调式模式启动以寻找异常原因", OlanExceptionAction.Warning));
    }

    public void Dispose()
    {
        _gameProcess?.Dispose();
    }
    #endregion
}