using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.Launcher;

/// <summary>
/// 单次游戏启动会话。每次启动应创建一个新实例。
/// </summary>
public sealed class LaunchService : IGameLauncher, IDisposable
{
    public event Action? GameStartedEvent;
    public event Action<int>? GameClosedEvent;
    public event Action<string>? GameOutputEvent;

    private readonly GameLauncher _gameLauncher = new();
    public string GameDirectory => _gameLauncher.GameDirectory;
    private readonly TaskInstance _task;
    private readonly CancellationToken _cancellationToken;
    private int _hasLaunched;
    private int _gameStarted;
    private bool _disposed;

    internal LaunchService(TaskInstance task)
    {
        _task = task;
        _cancellationToken = task.Token?.Token
            ?? throw new ArgumentException("启动任务必须由 TaskManager.CreateTask 创建。", nameof(task));
        _gameLauncher.GameClosedEvent += OnGameClosed;
        _gameLauncher.GameOutputEvent += OnGameOutput;
        _task.Canceled += OnTaskCanceled;
    }

    public async Task Play(string instanceId)
    {
        try
        {
            SetProgress("正在查找游戏实例...", 2);
            GameData? gameData = Init.GameDataManager.GetInstanceFromId(instanceId);
            if (gameData is null)
                throw new OlanException(
                    "启动失败",
                    $"没有找到匹配的且已安装的实例{Environment.NewLine}请检查你的输入{instanceId}是否正确");

            await Launch(gameData);
        }
        catch (Exception ex)
        {
            MarkFailed(ex);
            throw;
        }
    }

    public Task Play(GameData gameData, ServerInfo? serverInfo = null)
        => Launch(gameData, serverInfo);

    public async Task Play(
        UserVersion userVersion,
        ServerInfo? serverInfo = null,
        bool useRootMode = false)
    {
        try
        {
            SetProgress("正在查找或创建游戏实例...", 2);
            GameData gameData = await Init.GameDataManager
                .GetOrCreateInstanceAsync(userVersion)
                .WaitAsync(_cancellationToken);
            await Launch(gameData, serverInfo, useRootMode);
        }
        catch (Exception ex)
        {
            MarkFailed(ex);
            throw;
        }
    }

    public Task Stop()
    {
        ThrowIfDisposed();
        return _gameLauncher.Stop();
    }

    private async Task Launch(
        GameData gameData,
        ServerInfo? serverInfo = null,
        bool useRootMode = false)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _hasLaunched, 1) != 0)
            throw new InvalidOperationException("一个 LaunchService 实例只能启动一次游戏。");
        try
        {
            await PrepareGameData(gameData, _cancellationToken);
            await _gameLauncher.BasicLaunch(
                gameData,
                serverInfo,
                useRootMode,
                new Progress<(string Current, double Progress)>(p =>
                    SetProgress(p.Current, p.Progress)),
                _cancellationToken);
        }
        catch (OperationCanceledException) when (_task.Status == TaskInfoStatus.Canceled)
        {
            throw;
        }
        catch (Exception ex)
        {
            MarkFailed(ex);
            throw;
        }
    }

    private async Task PrepareGameData(GameData gameData, CancellationToken cancellationToken)
    {
        SetProgress("正在检查启动依赖...", 5);
        cancellationToken.ThrowIfCancellationRequested();
        string authlibPath = Path.Combine(Init.InstalledPath, "authlib.jar");
        if (!File.Exists(authlibPath))
        {
            SetProgress("正在下载登录依赖库 authlib-injector...", 10);
            await Init.Download.DownloadFile(
                "https://authlib-injector.yushi.moe/artifact/53/authlib-injector-1.2.5.jar",
                authlibPath,
                cancellationToken);
        }

        SetProgress("正在检查游戏语言设置...", 15);
        string optionsPath = Path.Combine(gameData.InstancePath, "options.txt");
        if (!File.Exists(optionsPath))
        {
            SetProgress("正在将游戏语言调整为简体中文...", 20);
            await File.WriteAllTextAsync(optionsPath, "lang:zh_CN", cancellationToken);
        }
    }

    private void SetProgress(string current, double progress)
    {
        if (_task.Status == TaskInfoStatus.Running)
            _task.SetProgress(current, progress);
    }

    private void MarkFailed(Exception exception)
    {
        if (exception is OperationCanceledException && _task.Status == TaskInfoStatus.Canceled)
            return;
        if (_task.Status == TaskInfoStatus.Running)
            _task.SetFailed(exception);
    }

    private void OnGameClosed(int exitCode)
    {
        bool wasCanceled = _task.Status == TaskInfoStatus.Canceled;
        if (_task.Status == TaskInfoStatus.Running)
        {
            if (exitCode == 0)
            {
                _task.SetProgress("游戏已正常退出", 100);
                _task.SetDone();
            }
            else
            {
                _task.SetFailed(new OlanException(
                    _gameStarted == 0 ? "启动失败" : "游戏异常退出",
                    $"游戏进程退出代码：{exitCode}"));
            }
        }

        // 强制结束进程在部分平台会返回 -1；用户主动取消属于正常操作，
        // 对外统一报告为正常退出，避免触发异常退出提示。
        GameClosedEvent?.Invoke(wasCanceled ? 0 : exitCode);
    }

    private void OnGameOutput(string output)
    {
        GameOutputEvent?.Invoke(output);
        if (output.Contains("Backend library: LWJGL version", StringComparison.Ordinal) &&
            Interlocked.Exchange(ref _gameStarted, 1) == 0)
        {
            SetProgress("游戏运行中", 100);
            GameStartedEvent?.Invoke();
        }
    }

    private void OnTaskCanceled()
    {
        _ = StopAfterCancellationAsync();
    }

    private async Task StopAfterCancellationAsync()
    {
        try
        {
            await _gameLauncher.Stop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"取消启动任务时停止游戏失败：{ex}");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _task.Canceled -= OnTaskCanceled;
        _gameLauncher.GameClosedEvent -= OnGameClosed;
        _gameLauncher.GameOutputEvent -= OnGameOutput;
        _gameLauncher.Dispose();
        GameStartedEvent = null;
        GameClosedEvent = null;
        GameOutputEvent = null;
        GC.SuppressFinalize(this);
    }
}
