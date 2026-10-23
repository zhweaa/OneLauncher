using OneLauncher.Interfaces;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.ViewModel.Page;
using OneLauncher.Views.Windows;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OneLauncher.Converters;
namespace OneLauncher.Views.Panes.PaneViewModels;
internal partial class DownloadPaneViewModel : BaseViewModel, IPaneHost
{
    private readonly GameDataManager _gameDataManager;
    private readonly UnitedCore _unitedCore;
    public Action OnPaneClose { get; init; } = () => { };
#if DEBUG
    // 供设计器预览
    public DownloadPaneViewModel()
    {
        VersionName = "1.21.5";
        IsAllowNeoforge = true;
        IsAllowFabric = true;
    }
#endif
    public DownloadPaneViewModel(
        VersionBasicInfo Version,
        GameDataManager gameDataManager,
        UnitedCore unitedCore,
        Action onCloseCallback)
    {
        this._gameDataManager = gameDataManager;
        this._unitedCore = unitedCore;
        this.OnPaneClose = onCloseCallback ?? (() => { });
        VersionName = Version.ID.ToString();
        thisVersionBasicInfo = Version;
        // 这个版本以下不支持模组加载器
        IsAllowFabric = new Version(Version.ID) < new System.Version("1.14") ? false : true;
        IsAllowNeoforge = new Version(Version.ID) < new System.Version("1.20.2") ? false : true;
        IsAllowForge = new Version(Version.ID) < new System.Version("1.2") ? false : true;
        IsAllowQuilt = new Version(Version.ID) < new System.Version("1.14") ? false : true;
    }
    private TaskInstance? task;
    public bool IsFinished => DownloadStatus == TaskInfoStatus.Finished;
    public bool IsFailed => DownloadStatus == TaskInfoStatus.Failed;
    public bool IsCanceled => DownloadStatus == TaskInfoStatus.Canceled;
    public string CancelLabel => IsCancelling ? "正在取消..." : "取消任务";
    public string StatusLabel => IsCancelling ? "正在取消" : IsDownloading ? "下载中" : DownloadStatus switch
    {
        TaskInfoStatus.Finished => "已完成",
        TaskInfoStatus.Failed => "下载失败",
        TaskInfoStatus.Canceled => "已取消",
        _ => "等待下载"
    };
    public Geometry StatusIcon => DownloadStatus switch
    {
        TaskInfoStatus.Finished => VectorIcons.StatusCompleted,
        TaskInfoStatus.Failed => VectorIcons.StatusFailed,
        TaskInfoStatus.Canceled => VectorIcons.StatusCanceled,
        _ => VectorIcons.StatusRunning
    };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusLabel)),
        NotifyCanExecuteChangedFor(nameof(ToDownloadCommand), nameof(ToCancellationDownloadTaskCommand))]
    private bool isDownloading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusLabel), nameof(CancelLabel)),
        NotifyCanExecuteChangedFor(nameof(ToCancellationDownloadTaskCommand))]
    private bool isCancelling;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusLabel), nameof(StatusIcon), nameof(IsFinished), nameof(IsFailed), nameof(IsCanceled)),
        NotifyCanExecuteChangedFor(nameof(ToDownloadCommand))]
    private TaskInfoStatus downloadStatus = TaskInfoStatus.Running;
    #region 数据绑定区
    [ObservableProperty] public bool _IsQuilt;
    [ObservableProperty] public bool _IsDownloadQuiltWithQSL = true;
    [ObservableProperty] public bool _IsAllowQuilt;
    [ObservableProperty] public bool _IsForge;
    [ObservableProperty] public bool _IsAllowForge;
    [ObservableProperty] public bool _IsUseRecommendedToInstallForge = true;
    [ObservableProperty] public bool _IsAllowFabric;
    [ObservableProperty] public bool _IsAllowNeoforge;
    [ObservableProperty] public string _VersionName;
    [ObservableProperty] public bool _IsMod;
    [ObservableProperty] public bool _IsNeoForge;
    [ObservableProperty] public bool _IsAllowToUseBetaNeoforge = false;
    [ObservableProperty] public bool _IsDownloadFabricWithAPI = true;
    [ObservableProperty] public bool _IsJava;
    [ObservableProperty] public bool _IsAllowDownloading = true; // 默认允许下载
    [ObservableProperty] public string _Dp = "下载未开始";
    [ObservableProperty] public string _Fs = "?/0";
    [ObservableProperty] public string _FileName = "操作文件名：（下载未开始）";
    [ObservableProperty] public double _CurrentProgress = 0;
    private VersionBasicInfo thisVersionBasicInfo;
    #endregion
    private bool CanDownload() => !IsDownloading && !IsFinished;
    private bool CanCancel() => IsDownloading && !IsCancelling && task?.Status == TaskInfoStatus.Running;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    public async Task ToDownload()
    {
        if (!CanDownload()) return;
        var downloadTask = _unitedCore.TaskManager.CreateTask($"下载 MC {VersionName}");
        task = downloadTask;
        var cancellation = downloadTask.Token!.Token;
        downloadTask.Canceled += OnTaskCanceled;
        DownloadStatus = TaskInfoStatus.Running;
        IsCancelling = false;
        IsDownloading = true;
        IsAllowDownloading = false;
        CurrentProgress = 0;
        Dp = "正在准备下载...";
        Fs = string.Empty;
        FileName = string.Empty;
        var VersionModType = new ModType()
        {
            IsFabric = IsMod,
            IsNeoForge = IsNeoForge,
            IsForge = IsForge,
            IsQuilt = IsQuilt
        };
        DateTime lastUpdateTime = DateTime.MinValue;
        TimeSpan _updateInterval = TimeSpan.FromMilliseconds(50);
        DownProgress? lastStage = null;

        // 回调在 UI 线程创建，结束后排队中的进度也不能覆盖终态。
        var progressReporter = new Progress<(DownProgress d, int a, int b, string c)>(p =>
        {
            if (!ReferenceEquals(task, downloadTask) || !IsDownloading || downloadTask.Status != TaskInfoStatus.Running)
                return;
            if (p.d == DownProgress.Done || p.d == DownProgress.Meta || p.d != lastStage || (DateTime.UtcNow - lastUpdateTime) > _updateInterval)
            {
                Dp = p.d switch
                {
                    DownProgress.Done => "正在登记游戏实例...",
                    DownProgress.Meta => "正在准备元数据",
                    DownProgress.DownAndInstModFiles => "下载并安装模组加载器...",
                    DownProgress.DownLog4j2 => "下载日志配置...",
                    DownProgress.DownLibs => "下载库文件...",
                    DownProgress.DownAssets => "下载资源文件...",
                    DownProgress.DownMain => "下载游戏主文件...",
                    DownProgress.Verify => "校验文件中...",
                    _ => "正在下载..."
                };
                CurrentProgress = p.a > 0 ? Math.Clamp(100d * p.b / p.a, 0, 99) : 0;
                downloadTask.SetProgress(Dp, CurrentProgress);
                lastUpdateTime = DateTime.UtcNow;
                lastStage = p.d;
            }
            Fs = p.d == DownProgress.Meta ? "" : $"{p.b}/{p.a}";
            FileName = p.c;
        });
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"开始下载 MC {VersionName}。", openTaskCenterAfterClick: true));
        try
        {
            var content = await Task.Run(async () =>
            {
                cancellation.ThrowIfCancellationRequested();
                var info = await DownloadInfo.Create(
                        VersionName,
                        VersionModType,
                        IsAllowToUseBetaNeoforge,
                        IsUseRecommendedToInstallForge,
                        IsDownloadFabricWithAPI,
                        IsJava,
                        IsDownloadQuiltWithQSL
                        );
                cancellation.ThrowIfCancellationRequested();
                await _unitedCore.VersionInstallerService.InstallNewVersion(info, progressReporter, cancellation);
                return info;
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            await _gameDataManager.AddGameDataAsync(content.UserInfo);
            cancellation.ThrowIfCancellationRequested();
            await _gameDataManager.SetDefaultInstanceAsync(content.UserInfo.InstanceId);
            cancellation.ThrowIfCancellationRequested();
            Dp = "下载完成";
            CurrentProgress = 100;
            downloadTask.SetProgress(Dp, 100);
            downloadTask.SetDone();
            DownloadStatus = TaskInfoStatus.Finished;
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"“{content.UserInfo.Name}”已成功创建并设为默认启动项。", openTaskCenterAfterClick: true));
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            downloadTask.Cancel();
            DownloadStatus = TaskInfoStatus.Canceled;
            Dp = "下载已取消";
        }
        catch (Exception ex)
        {
            downloadTask.SetFailed(ex);
            DownloadStatus = TaskInfoStatus.Failed;
            Dp = ex.Message;
            if (ex is OlanException known) await OlanExceptionWorker.ForOlanException(known);
            else await OlanExceptionWorker.ForUnknowException(ex);
        }
        finally
        {
            downloadTask.Canceled -= OnTaskCanceled;
            IsDownloading = false;
            IsCancelling = false;
            IsAllowDownloading = !IsFinished;
        }
    }

    private void OnTaskCanceled()
    {
        var canceledTask = task;
        void Update()
        {
            if (!ReferenceEquals(task, canceledTask) || !IsDownloading) return;
            IsCancelling = true;
            Dp = "正在取消下载...";
        }
        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void ToCancellationDownloadTask()
    {
        // TaskInstance 持有同一个 CancellationTokenSource；通过它取消即可，避免重复触发取消回调。
        task?.Cancel();
    }
    [RelayCommand]
    public void ClosePane()
    {
        OnPaneClose();
        //WeakReferenceMessenger.Default.Send(new DownloadPageClosePaneControlMessage());
    }
    [RelayCommand]
    public void PopUp()
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        new PopUpPane(new DownloadPane() { DataContext = this}).Show();
    }
    //public void CheckOnWeb()
    //{
    //    Process.Start(new ProcessStartInfo
    //    {
    //        FileName = $"https://zh.minecraft.wiki/w/Java版{VersionName}",
    //        UseShellExecute = true
    //    });
    //}
}
