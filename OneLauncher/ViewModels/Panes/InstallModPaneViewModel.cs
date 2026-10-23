using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Resource.ModService.Modrinth;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Interfaces;
using OneLauncher.ViewModel.Page;
using OneLauncher.Views.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OneLauncher.Converters;

namespace OneLauncher.Views.Panes.PaneViewModels;

internal partial class InstallModPaneViewModel : BaseViewModel, IPaneHost
{
    private readonly GameDataManager _gameDataManager = null!;
    private readonly UnitedCore _unitedCore = null!;
    private readonly DBManager _configManager = null!;
    private readonly ResourceItem _resource = null!;
    private TaskInstance? _task;

    public Action OnPaneClose { get; init; } = () => { };

#if DEBUG
    public InstallModPaneViewModel()
    {
        if (Design.IsDesignMode) ModName = "暮色森林";
    }
#endif

    public InstallModPaneViewModel(
        ResourceItem item,
        GameDataManager gameDataManager,
        UnitedCore unitedCore,
        DBManager configManager,
        Action onCloseCallback)
    {
        _resource = item;
        _gameDataManager = gameDataManager;
        _unitedCore = unitedCore;
        _configManager = configManager;
        ModName = item.Title;
        SupportVersions = new List<string>(item.SupportVersions);
        SupportVersions.Reverse();
        AvailableGameData = gameDataManager.AllGameData;
        OnPaneClose = onCloseCallback ?? (() => { });
    }

    public bool IsMod => _resource == null || _resource.Type == ResourceType.Mod;
    public bool IsShaderPack => _resource?.Type == ResourceType.ShaderPack;
    public string ResourceName => IsMod ? "模组" : "光影包";
    public string PaneTitle => $"安装{ResourceName} · {ModName}";
    public bool HasInstances => AvailableGameData.Count > 0;
    public bool CanEdit => !IsInstalling;
    public string CancelLabel => IsCancelling ? "正在取消..." : "取消安装";
    public string StatusLabel => IsCancelling ? "正在取消" : IsInstalling ? "安装中" : InstallationStatus switch
    {
        TaskInfoStatus.Finished => "已完成",
        TaskInfoStatus.Failed => "安装失败",
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
    public IBrush StatusSurfaceBrush => new SolidColorBrush(InstallationStatus switch
    {
        TaskInfoStatus.Finished => Color.Parse("#19107C10"),
        TaskInfoStatus.Failed => Color.Parse("#18C42B1C"),
        TaskInfoStatus.Canceled => Color.Parse("#1FD18B00"),
        _ => Color.Parse("#14007AFF")
    });
    public Geometry StatusIcon => InstallationStatus switch
    {
        TaskInfoStatus.Finished => VectorIcons.StatusCompleted,
        TaskInfoStatus.Failed => VectorIcons.StatusFailed,
        TaskInfoStatus.Canceled => VectorIcons.StatusCanceled,
        _ => VectorIcons.StatusRunning
    };

    [ObservableProperty] private string modName = string.Empty;
    [ObservableProperty] private List<string> supportVersions = new();
    [ObservableProperty] private List<GameData> availableGameData = new();
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ToInstallCommand))]
    private GameData? selectedGameData;
    [ObservableProperty] private bool isShowMoreInfo = true;
    [ObservableProperty] private bool isICS = true;
    [ObservableProperty] private string dp = "等待安装";
    [ObservableProperty] private string fs = string.Empty;
    [ObservableProperty] private double currentProgress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit), nameof(StatusLabel)),
        NotifyCanExecuteChangedFor(nameof(ToInstallCommand), nameof(CancelInstallCommand))]
    private bool isInstalling;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusLabel), nameof(CancelLabel)),
        NotifyCanExecuteChangedFor(nameof(CancelInstallCommand))]
    private bool isCancelling;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusLabel), nameof(StatusBrush), nameof(StatusSurfaceBrush), nameof(StatusIcon))]
    private TaskInfoStatus installationStatus = TaskInfoStatus.Running;

    private bool CanInstall() => !IsInstalling && SelectedGameData != null && HasInstances;
    private bool CanCancel() => IsInstalling && !IsCancelling && _task?.Status == TaskInfoStatus.Running;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    public async Task ToInstall()
    {
        if (IsInstalling) return;
        var selected = SelectedGameData;
        if (selected == null)
        {
            Notify("请先选择一个游戏实例！");
            return;
        }
        var instance = _gameDataManager.GetInstanceFromId(selected.InstanceId);
        if (instance == null)
        {
            AvailableGameData = _gameDataManager.AllGameData;
            OnPropertyChanged(nameof(HasInstances));
            SelectedGameData = null;
            Notify("该游戏实例已不存在，请重新选择。");
            return;
        }
        if (string.IsNullOrWhiteSpace(instance.VersionId))
        {
            Notify("该游戏实例缺少 Minecraft 版本信息。");
            return;
        }
        if (IsMod && _resource.SupportModType != instance.ModLoader)
        {
            Notify($"此模组不支持 {instance.ModLoader} 加载器。");
            return;
        }
        if (IsMod && !SupportVersions.Contains(instance.VersionId))
        {
            Notify($"此模组不支持游戏版本 {instance.VersionId}。");
            return;
        }

        var task = _unitedCore.TaskManager.CreateTask($"安装 {ModName} {ResourceName}");
        _task = task;
        task.Canceled += OnTaskCanceled;
        InstallationStatus = TaskInfoStatus.Running;
        IsCancelling = false;
        IsInstalling = true;
        IsShowMoreInfo = false;
        CurrentProgress = 0;
        Fs = string.Empty;
        Dp = "正在准备安装...";
        Notify($"开始安装{ResourceName}“{ModName}”。", openTaskCenter: true);
        try
        {
            var progress = new Progress<(long all, long done, double percentage, string fileName)>(p =>
            {
                void Update()
                {
                    if (!ReferenceEquals(_task, task) || !IsInstalling || task.Status != TaskInfoStatus.Running) return;
                    // 后端在校验前可能报告 100%；成功返回后才显示最终完成。
                    CurrentProgress = Math.Clamp(p.percentage, 0, 99);
                    Fs = p.all > 0 ? $"{p.done}/{p.all}" : string.Empty;
                    Dp = p.fileName;
                    task.SetProgress($"{Fs} {Dp}".Trim(), CurrentProgress);
                }
                if (Dispatcher.UIThread.CheckAccess()) Update();
                else Dispatcher.UIThread.Post(Update);
            });

            if (IsMod)
                await _unitedCore.ResourceInstallerService.InstallMod(
                    progress, _resource.ID, instance, instance.ModLoader,
                    includeDependencies: IsICS,
                    verifySha1: _configManager.Data.OlanSettings.IsSha1Enabled,
                    cancellationToken: task.Token!.Token);
            else
                await _unitedCore.ResourceInstallerService.InstallShaderPack(
                    progress, _resource.ID, instance,
                    verifySha1: _configManager.Data.OlanSettings.IsSha1Enabled,
                    cancellationToken: task.Token!.Token);

            task.Token!.Token.ThrowIfCancellationRequested();
            CurrentProgress = 100;
            Fs = "完成";
            Dp = $"{ResourceName}安装完成";
            task.SetProgress(Dp, 100);
            task.SetDone();
            InstallationStatus = TaskInfoStatus.Finished;
            Notify($"“{ModName}”安装成功！", openTaskCenter: true);
        }
        catch (OperationCanceledException)
        {
            SetCanceled(task);
        }
        catch (Exception) when (task.Token!.IsCancellationRequested)
        {
            SetCanceled(task);
        }
        catch (OlanException ex)
        {
            SetFailed(task, ex);
            await OlanExceptionWorker.ForOlanException(ex);
        }
        catch (Exception ex)
        {
            SetFailed(task, ex);
            await OlanExceptionWorker.ForUnknowException(ex);
        }
        finally
        {
            task.Canceled -= OnTaskCanceled;
            IsInstalling = false;
            IsCancelling = false;
            IsShowMoreInfo = true;
        }
    }

    private void SetCanceled(TaskInstance task)
    {
        task.Cancel();
        InstallationStatus = TaskInfoStatus.Canceled;
        Dp = "安装已取消";
    }

    private void SetFailed(TaskInstance task, Exception exception)
    {
        task.SetFailed(exception);
        InstallationStatus = TaskInfoStatus.Failed;
        Dp = exception.Message;
    }

    private static void Notify(string text, bool openTaskCenter = false)
        => WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(text, openTaskCenterAfterClick: openTaskCenter));

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void CancelInstall() => _task?.Cancel();

    private void OnTaskCanceled()
    {
        var task = _task;
        void Update()
        {
            if (!ReferenceEquals(_task, task) || !IsInstalling) return;
            IsCancelling = true;
            Dp = "正在取消安装...";
        }
        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    [RelayCommand]
    public void ClosePane()
    {
        if (IsInstalling) _task?.Cancel();
        OnPaneClose();
    }
}

