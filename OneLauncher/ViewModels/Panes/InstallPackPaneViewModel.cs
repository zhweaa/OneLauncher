using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.DefaultComponent.Resource.Pack;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Interfaces;
using OneLauncher.ViewModel.Page;
using OneLauncher.Views.ViewModels;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OneLauncher.Converters;

namespace OneLauncher.Views.Panes.PaneViewModels;

internal sealed partial class InstallPackPaneViewModel : BaseViewModel, IPaneHost
{
    private readonly ResourceItem _resource;
    private readonly UnitedCore _unitedCore;
    private static readonly HttpClient IconClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private ModpackDownload? _package;
    private GameData? _importedInstance;
    private CancellationTokenSource? _infoCancellation;
    private TaskInstance? _task;
    private bool _closed;

    public Action OnPaneClose { get; init; }
    public string PaneTitle => $"安装整合包 · {_resource.Title}";
    public string Description => _resource.Description;
    public bool IsBusy => IsLoading || IsInstalling;
    public bool IsCompleted => InstallationStatus == TaskInfoStatus.Finished || (_importedInstance != null && !IsBusy);
    public bool ShowInstall => !IsBusy && !IsCompleted;
    public bool CanReload => !HasPackage && !IsBusy;
    public string CancelLabel => IsCancelling ? "正在取消..." : IsLoading ? "取消加载" : "取消安装";
    public string StatusLabel => IsCancelling ? "正在取消" : IsLoading ? "获取信息中" : IsInstalling ? "安装中" : InstallationStatus switch
    {
        TaskInfoStatus.Finished => "已完成",
        TaskInfoStatus.Failed => "失败",
        TaskInfoStatus.Canceled => "已取消",
        _ => "等待安装"
    };
    public IBrush StatusBrush => InstallationStatus switch
    {
        TaskInfoStatus.Finished => Brushes.ForestGreen,
        TaskInfoStatus.Failed => Brushes.Firebrick,
        TaskInfoStatus.Canceled => Brushes.DarkOrange,
        _ => Brushes.DodgerBlue
    };
    public IBrush StatusSurfaceBrush => new SolidColorBrush(Color.Parse(InstallationStatus switch
    {
        TaskInfoStatus.Finished => "#19107C10",
        TaskInfoStatus.Failed => "#18C42B1C",
        TaskInfoStatus.Canceled => "#1FD18B00",
        _ => "#14007AFF"
    }));
    public Geometry StatusIcon => InstallationStatus switch
    {
        TaskInfoStatus.Finished => VectorIcons.StatusCompleted,
        TaskInfoStatus.Failed => VectorIcons.StatusFailed,
        TaskInfoStatus.Canceled => VectorIcons.StatusCanceled,
        _ => VectorIcons.StatusRunning
    };

    public InstallPackPaneViewModel(ResourceItem item, UnitedCore unitedCore, Action onCloseCallback)
    {
        _resource = item;
        _unitedCore = unitedCore;
        OnPaneClose = onCloseCallback;
        packName = item.Title;
    }

    [ObservableProperty] private string packName;
    [ObservableProperty] private string minecraftVersion = string.Empty;
    [ObservableProperty] private string loaderInfo = string.Empty;
    [ObservableProperty] private string currentText = "正在获取整合包信息...";
    [ObservableProperty] private double currentProgress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanReload)), NotifyCanExecuteChangedFor(nameof(ToInstallCommand))]
    private bool hasPackage;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(ShowInstall), nameof(CanReload), nameof(StatusLabel), nameof(CancelLabel)),
        NotifyCanExecuteChangedFor(nameof(ToInstallCommand), nameof(CancelInstallCommand))]
    private bool isLoading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsCompleted), nameof(ShowInstall), nameof(CanReload), nameof(StatusLabel), nameof(CancelLabel)),
        NotifyCanExecuteChangedFor(nameof(ToInstallCommand), nameof(CancelInstallCommand))]
    private bool isInstalling;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusLabel), nameof(CancelLabel)),
        NotifyCanExecuteChangedFor(nameof(CancelInstallCommand))]
    private bool isCancelling;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusLabel), nameof(StatusBrush), nameof(StatusSurfaceBrush), nameof(StatusIcon), nameof(IsCompleted), nameof(ShowInstall)),
        NotifyCanExecuteChangedFor(nameof(ToInstallCommand))]
    private TaskInfoStatus installationStatus = TaskInfoStatus.Running;

    [RelayCommand]
    public async Task LoadInfo()
    {
        if (_closed || IsBusy || HasPackage) return;
        using var cancellation = new CancellationTokenSource();
        _infoCancellation = cancellation;
        IsCancelling = false;
        IsLoading = true;
        InstallationStatus = TaskInfoStatus.Running;
        CurrentText = "正在获取整合包信息...";
        try
        {
            var package = await _unitedCore.ResourceInstallerService.GetModpackInfoAsync(_resource.ID, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested)
            {
                package.Dispose();
                cancellation.Token.ThrowIfCancellationRequested();
                return;
            }
            _package = package;
            PackName = package.Parser.GetName();
            MinecraftVersion = package.Parser.GetMinecraftVersion();
            var (loader, version) = package.Parser.GetLoaderInfo();
            LoaderInfo = loader == ModEnum.none ? "原版" : $"{loader} {version}";
            HasPackage = true;
            CurrentText = "准备就绪，安装后将创建新的游戏实例。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            InstallationStatus = TaskInfoStatus.Canceled;
            CurrentText = "信息加载已取消";
        }
        catch (Exception ex)
        {
            InstallationStatus = TaskInfoStatus.Failed;
            CurrentText = ex.Message;
            if (!_closed) await ShowError(ex);
        }
        finally
        {
            _infoCancellation = null;
            IsLoading = false;
            IsCancelling = false;
        }
    }

    private bool CanInstall() => !_closed && HasPackage && !IsBusy && !IsCompleted;
    private bool CanCancel() => IsBusy && !IsCancelling;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    public async Task ToInstall()
    {
        if (!CanInstall() || _package == null) return;
        var task = _unitedCore.TaskManager.CreateTask($"安装整合包 {PackName}");
        _task = task;
        task.Canceled += OnTaskCanceled;
        InstallationStatus = TaskInfoStatus.Running;
        IsCancelling = false;
        IsInstalling = true;
        CurrentProgress = 0;
        CurrentText = "正在准备安装...";
        Notify($"开始安装整合包“{PackName}”。");
        try
        {
            // 图标是可选内容；提前获取，使取消发生在创建实例之前。
            var iconBytes = await GetIconAsync(task.Token!.Token);
            bool acceptingProgress = true;
            var progress = new Progress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>(p =>
            {
                void Update()
                {
                    if (!acceptingProgress || !ReferenceEquals(_task, task) || !IsInstalling || task.Status != TaskInfoStatus.Running) return;
                    CurrentProgress = p.AllFiles > 0 ? Math.Clamp(100d * p.DownedFiles / p.AllFiles, 0, 99) : 0;
                    CurrentText = p.DowingFileName;
                    task.SetProgress(CurrentText, CurrentProgress);
                }
                if (Dispatcher.UIThread.CheckAccess()) Update();
                else Dispatcher.UIThread.Post(Update);
            });
            var gameData = await _unitedCore.ResourceInstallerService.ImportModpack(_package, progress, task.Token!.Token);
            _importedInstance = gameData;
            acceptingProgress = false;
            // 返回的实例已登记；可选图标失败不把成功的导入变成可重复安装的失败任务。
            if (iconBytes != null) await SaveIconAsync(gameData, iconBytes);
            task.Token!.Token.ThrowIfCancellationRequested();
            CurrentProgress = 100;
            CurrentText = "整合包导入完成，可在游戏数据中查看新实例。";
            task.SetProgress(CurrentText, 100);
            task.SetDone();
            InstallationStatus = TaskInfoStatus.Finished;
            Notify($"“{PackName}”整合包安装成功！");
        }
        catch (OperationCanceledException)
        {
            task.Cancel();
            InstallationStatus = TaskInfoStatus.Canceled;
            CurrentText = _importedInstance == null ? "安装已取消" : "任务已取消，已导入的游戏实例保留。";
        }
        catch (Exception) when (task.Token!.IsCancellationRequested)
        {
            task.Cancel();
            InstallationStatus = TaskInfoStatus.Canceled;
            CurrentText = _importedInstance == null ? "安装已取消" : "任务已取消，已导入的游戏实例保留。";
        }
        catch (Exception ex)
        {
            task.SetFailed(ex);
            InstallationStatus = TaskInfoStatus.Failed;
            CurrentText = ex.Message;
            if (!_closed) await ShowError(ex);
        }
        finally
        {
            task.Canceled -= OnTaskCanceled;
            IsInstalling = false;
            IsCancelling = false;
            if (_closed) _package?.Dispose();
        }
    }

    private async Task<byte[]?> GetIconAsync(CancellationToken token)
    {
        if (_resource.IconUrl is not { } url || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            return null;
        CurrentText = "正在获取实例图标...";
        _task!.SetProgress(CurrentText, 0);
        try
        {
            using var response = await IconClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            using var stream = new MemoryStream(bytes);
            using var bitmap = new Bitmap(stream); // 验证图片后才覆盖已有图标。
            token.ThrowIfCancellationRequested();
            return bytes;
        }
        catch (Exception) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            NotifyIconWarning();
            return null;
        }
    }

    private async Task SaveIconAsync(GameData gameData, byte[] bytes)
    {
        string iconPath = Path.Combine(gameData.InstancePath, ".olc", "customicon");
        string temporaryPath = iconPath + $".{Guid.NewGuid():N}.tmp";
        CurrentText = "正在设置实例图标...";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
            await File.WriteAllBytesAsync(temporaryPath, bytes);
            File.Move(temporaryPath, iconPath, overwrite: true);
            // 实例登记早于图标写入，通过现有数据事件刷新已打开的实例列表。
            await _unitedCore.GameDataManager.Save();
        }
        catch (Exception) { NotifyIconWarning(); }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void NotifyIconWarning()
    {
        if (!_closed) WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
            "整合包图标未能自动设置，可在编辑实例中更换图标。", NotificationType.Warning));
    }

    private void OnTaskCanceled()
    {
        var task = _task;
        void Update()
        {
            if (!ReferenceEquals(_task, task) || !IsInstalling) return;
            IsCancelling = true;
            CurrentText = "正在取消安装...";
        }
        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void CancelInstall()
    {
        if (IsLoading)
        {
            IsCancelling = true;
            _infoCancellation?.Cancel();
        }
        else _task?.Cancel();
    }

    [RelayCommand]
    public void ClosePane()
    {
        _closed = true;
        _infoCancellation?.Cancel();
        _task?.Cancel();
        // 安装尚未退出时保留 ZIP，避免解压过程与删除临时文件竞争。
        if (!IsInstalling) _package?.Dispose();
        OnPaneClose();
    }

    private static Task ShowError(Exception ex) => ex is OlanException known
        ? OlanExceptionWorker.ForOlanException(known) : OlanExceptionWorker.ForUnknowException(ex);

    private static void Notify(string text)
        => WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(text, openTaskCenterAfterClick: true));
}
