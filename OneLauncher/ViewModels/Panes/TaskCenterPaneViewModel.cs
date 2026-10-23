using OneLauncher.Interfaces;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Core.Global;
using OneLauncher.ViewModel.Page;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OneLauncher.Views.Panes.PaneViewModels;

internal sealed partial class TaskCenterPaneViewModel : BaseViewModel, IPaneHost, IDisposable
{
    private readonly TaskManager _taskManager;
    public Action OnPaneClose { get; init; } = () => { };
    private volatile bool _disposed;

    [ObservableProperty]
    private List<TaskInstanceItemViewModel> tasks = new();

    [ObservableProperty]
    private bool hasTasks;

    public TaskCenterPaneViewModel(UnitedCore unitedCore, Action onCloseCallback)
    {
        _taskManager = unitedCore.TaskManager;
        OnPaneClose = onCloseCallback ?? (() => { });
        _taskManager.OnTaskCreate += OnTaskCreate;
        RefList();
    }

    private void OnTaskCreate()
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefList();
        else
            Dispatcher.UIThread.Post(RefList);
    }

    private void RefList()
    {
        if (_disposed)
            return;

        foreach (var item in Tasks)
            item.Dispose();

        // 使用完整队列快照重建列表，最新创建的任务显示在顶部。
        Tasks = _taskManager.TaskQueue.ToArray().Reverse()
            .Select(task => new TaskInstanceItemViewModel(task)).ToList();
        HasTasks = Tasks.Count > 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _taskManager.OnTaskCreate -= OnTaskCreate;
        foreach (var item in Tasks)
            item.Dispose();
    }

    [RelayCommand]
    private void ClosePane() => OnPaneClose();
}

internal sealed partial class TaskInstanceItemViewModel : ObservableObject, IDisposable
{
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#FF007AFF"));
    private static readonly IBrush CanceledBrush = new SolidColorBrush(Color.Parse("#FF8A5A00"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#FFC42B1C"));
    private static readonly IBrush FinishedBrush = new SolidColorBrush(Color.Parse("#FF107C10"));
    private static readonly IBrush RunningSurfaceBrush = new SolidColorBrush(Color.Parse("#14007AFF"));
    private static readonly IBrush CanceledSurfaceBrush = new SolidColorBrush(Color.Parse("#1FD18B00"));
    private static readonly IBrush FailedSurfaceBrush = new SolidColorBrush(Color.Parse("#18C42B1C"));
    private static readonly IBrush FinishedSurfaceBrush = new SolidColorBrush(Color.Parse("#19107C10"));

    private readonly TaskInstance _task;
    private volatile bool _disposed;
    private string _statusText = string.Empty;
    private string _currentText = string.Empty;
    private double _progress;
    private IBrush _progressBrush = RunningBrush;

    public TaskInstance Task => _task;
    public string Name => _task.Name;
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }
    public string CurrentText
    {
        get => _currentText;
        private set => SetProperty(ref _currentText, value);
    }
    public bool IsRunning => _task.Status == TaskInfoStatus.Running;
    public bool IsCanceled => _task.Status == TaskInfoStatus.Canceled;
    public bool IsFailed => _task.Status == TaskInfoStatus.Failed;
    public bool IsFinished => _task.Status == TaskInfoStatus.Finished;
    public bool CanCancel => _task.Status == TaskInfoStatus.Running;
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }
    public IBrush ProgressBrush
    {
        get => _progressBrush;
        private set => SetProperty(ref _progressBrush, value);
    }
    public IBrush ProgressSurfaceBrush => _task.Status switch
    {
        TaskInfoStatus.Canceled => CanceledSurfaceBrush,
        TaskInfoStatus.Failed => FailedSurfaceBrush,
        TaskInfoStatus.Finished => FinishedSurfaceBrush,
        _ => RunningSurfaceBrush
    };

    public TaskInstanceItemViewModel(TaskInstance task)
    {
        _task = task;
        _task.ProgressChanged += OnProgressChanged;
        _task.Finished += OnFinished;
        _task.Failed += OnFailed;
        _task.Canceled += OnCanceled;
        RefreshStatus();
    }

    private void OnProgressChanged(double _)
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshStatus();
        else
            Dispatcher.UIThread.Post(RefreshStatus);
    }

    private void OnFinished() => RefreshStatusFromEvent();
    private void OnFailed() => RefreshStatusFromEvent();
    private void OnCanceled() => RefreshStatusFromEvent();

    private void RefreshStatusFromEvent()
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshStatus();
        else
            Dispatcher.UIThread.Post(RefreshStatus);
    }

    public void RefreshStatus()
    {
        if (_disposed)
            return;

        var status = _task.Status switch
        {
            TaskInfoStatus.Running => "运行中",
            TaskInfoStatus.Canceled => "已取消",
            TaskInfoStatus.Failed => "异常",
            TaskInfoStatus.Finished => "已完成",
            _ => "未知状态"
        };

        StatusText = status;
        CurrentText = _task.Exception?.Message
            ?? (string.IsNullOrWhiteSpace(_task.Current) ? "等待任务进度..." : _task.Current);
        Progress = Math.Clamp(_task.Progress, 0, 100);
        ProgressBrush = _task.Status switch
        {
            TaskInfoStatus.Canceled => CanceledBrush,
            TaskInfoStatus.Failed => FailedBrush,
            TaskInfoStatus.Finished => FinishedBrush,
            _ => RunningBrush
        };
        OnPropertyChanged(nameof(ProgressSurfaceBrush));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsCanceled));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(CanCancel));
    }

    [RelayCommand]
    private void CancelTask()
    {
        if (CanCancel)
            _task.Cancel();
    }

    public void Dispose()
    {
        _disposed = true;
        _task.ProgressChanged -= OnProgressChanged;
        _task.Finished -= OnFinished;
        _task.Failed -= OnFailed;
        _task.Canceled -= OnCanceled;
    }
}
