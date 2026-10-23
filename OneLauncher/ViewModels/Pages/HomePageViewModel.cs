using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.ImportPCL2Version;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OneLauncher.ViewModel.Page;
using OneLauncher.Interfaces;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Core.DefaultComponent.Resource.Pack;

namespace OneLauncher.Views.ViewModels;

internal partial class HomePageViewModel : BaseViewModel, IPageHost, IListTemplate<GameData>
{
    private GameDataManager _gameDataManager => _unitedCore.GameDataManager;
    private DBManager _configManager;
    private readonly UnitedCore _unitedCore;
    private readonly PowerPlayPaneViewModelFactory _mctVMFactory;
    private readonly TaskCenterPaneViewModelFactory _taskCenterVMFactory;
    private readonly INewReader _newsReader;

    [ObservableProperty] private ObservableCollection<GameData> items = new();
    [ObservableProperty] private GameData? selectedGameData = null;
    [ObservableProperty] private Bitmap gameIcon;

    [ObservableProperty] private bool isPaneShow;
    [ObservableProperty] private UserControl? paneContent;

    [ObservableProperty] private bool _isNewsLoading;
    [ObservableProperty] private MinecraftNew _currentNews;
    public HomePageViewModel(
        DBManager configManager,
        UnitedCore unitedCore,
        PowerPlayPaneViewModelFactory powerPlayPaneViewModelFactory,
        TaskCenterPaneViewModelFactory taskCenterPaneViewModelFactory
        )
    {
        this._configManager = configManager;
        this._unitedCore = unitedCore;
        this._mctVMFactory = powerPlayPaneViewModelFactory;
        this._taskCenterVMFactory = taskCenterPaneViewModelFactory;
        _ = RefList();
        _gameDataManager.OnDataChanged += OnGameDataChanged;
        if (_configManager.Data.OlanSettings.DefaultInstanceID != null)
            SelectedGameData = _gameDataManager.GetInstanceFromId(_configManager.Data.OlanSettings.DefaultInstanceID);
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 提前初始化联机模块
        _connentServiceInitializationTasker = _mctVMFactory.CreateAsync(() => IsPaneShow = false);

        _newsReader = new MinecraftNewsReader();
        if(!Design.IsDesignMode)
        _ = InitializeNewsAsync();

        if (!Init.InstalledPath.All(c => c < 128))
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage("当前安装路径包含非ASCII字符，可能导致游戏出现未知问题",NotificationType.Warning));

        _ = Task.Run(async () =>
        {
            if (await AutoUpdate.CheckUpdate())
            {
                WeakReferenceMessenger.Default.Send(
                    new MainWindowShowFlyoutMessage("检测到新版本，建议前往GitHub下载最新版本", NotificationType.Information));
            }
        });

        GameIcon = GameDataItem.GetGameDataIcon(SelectedGameData);
    }
    public Task RefList()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildList();
            return Task.CompletedTask;
        }
        return Dispatcher.UIThread.InvokeAsync(RebuildList).GetTask();
    }

    private void RebuildList() => Items = new ObservableCollection<GameData>(_gameDataManager.AllGameData);

    private void OnGameDataChanged() => _ = RefList();

    partial void OnSelectedGameDataChanged(GameData? value)
    {
        GameIcon = GameDataItem.GetGameDataIcon(value);
    }
    [RelayCommand]
    private void OpenTaskCenter()
    {
        PaneContent = new TaskCenterPane
        {
            DataContext = _taskCenterVMFactory.Create(() => IsPaneShow = false)
        };
        IsPaneShow = true;
    }

    partial void OnPaneContentChanging(UserControl? value)
    {
        if (PaneContent?.DataContext is TaskCenterPaneViewModel taskCenter)
            taskCenter.Dispose();
    }

    partial void OnIsPaneShowChanged(bool value)
    {
        if (!value && PaneContent?.DataContext is TaskCenterPaneViewModel taskCenter)
            taskCenter.Dispose();
    }
    [RelayCommand]
    public void Launch()
    {
        if(SelectedGameData == null)
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("请选择一个游戏数据实例！", Avalonia.Controls.Notifications.NotificationType.Warning));
            return;
        }
        _configManager.Data.OlanSettings.DefaultInstanceID = SelectedGameData.InstanceId;
        _ = _configManager.Save();
        _ = Game.EasyGameLauncher(_unitedCore, SelectedGameData, null, false);
    }
    #region 其他Item
    [RelayCommand]
    public async Task ImportVersionByPCL2()
    {
        TaskInstance? task = null;
        try
        {
            var topLevel = TopLevel.GetTopLevel(MainWindow.mainwindow);
            if (topLevel?.StorageProvider is { } storageProvider && storageProvider.CanOpen)
            {
                var options = new FolderPickerOpenOptions
                {
                    Title = "选择你的PCL2版本文件夹",
                    AllowMultiple = false,
                };
                var files = await storageProvider.OpenFolderPickerAsync(options);
                var selectedFile = files.FirstOrDefault();

                if (files == null || !files.Any() || selectedFile == null)
                    return;

                string path = selectedFile.Path.LocalPath;
                if (!File.Exists(Path.Combine(path, "PCL", "Setup.ini")))
                {
                    WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("这不是有效的PCL版本文件夹", NotificationType.Warning, title:"导入失败"));
                    return;
                }
                task = _unitedCore.TaskManager.CreateTask("PCL2版本导入");
                var token = task.Token!.Token;
                task.SetProgress("正在分析PCL2实例", 0);
                OpenTaskCenter();
                var progress = new Progress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>(p =>
                {
                    // 下载器也会报告 Done；只有整个导入返回后才能标记完成。
                    if (task.Status != TaskInfoStatus.Running || p.Title == DownProgress.Done)
                        return;
                    var stage = p.Title switch
                    {
                        DownProgress.Meta => "分析/迁移PCL2实例",
                        DownProgress.DownMain => "下载游戏主文件",
                        DownProgress.DownLibs => "下载依赖库",
                        DownProgress.DownAndInstModFiles => "安装模组加载器",
                        DownProgress.DownAssets => "下载资源文件",
                        DownProgress.DownLog4j2 => "下载日志配置",
                        DownProgress.Verify => "校验文件",
                        _ => "正在导入"
                    };
                    double percentage = p.AllFiles > 0
                        ? Math.Clamp((double)p.DownedFiles / p.AllFiles * 100, 0, 100) : 0;
                    task.SetProgress($"{stage} [{p.DownedFiles}/{p.AllFiles}] {p.DowingFileName}", percentage);
                });
                // 本地文件迁移包含同步复制，放到后台以保持任务中心可响应取消。
                await Task.Run(() => new PCL2Importer(progress, token).ImportAsync(path), token);
                token.ThrowIfCancellationRequested();
                task.SetProgress("导入完成", 100);
                task.SetDone();
            }
        }
        catch (OperationCanceledException) when (task?.Token?.IsCancellationRequested == true)
        {
            task.Cancel();
        }
        catch (Exception ex) when (task != null)
        {
            task.SetFailed(ex);
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex, () => _ = ImportVersionByPCL2());
        }
        catch (Exception ex)
        {
            await OlanExceptionWorker.ForUnknowException(ex, () => _ = ImportVersionByPCL2());
        }
    }
    private PowerPlayPane? _powerPlayGo;
    private Task<PowerPlayPaneViewModel> _connentServiceInitializationTasker;
    [RelayCommand]
    public async Task PowerPlay()
    {
        // 只有当第一次点击时才创建
        if (_powerPlayGo == null)
        {
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage("正在加载联机模块，请稍后..."));

            try
            {
                // 外部创建好
                var viewModel = await _connentServiceInitializationTasker;
                _powerPlayGo = new PowerPlayPane { DataContext = viewModel };
            }
            catch (OlanException ex)
            {
                await OlanExceptionWorker.ForOlanException(ex);
                IsPaneShow = false; // 出错时隐藏Pane
                return;
            }
            catch (Exception ex)
            {
                await OlanExceptionWorker.ForUnknowException(ex);
                IsPaneShow = false; // 出错时隐藏Pane
                return;
            }
        }

        // 将已经创建好的、包含完整数据的Pane设置为内容并显示
        PaneContent = _powerPlayGo;
        IsPaneShow = true;
    }
    [RelayCommand]
    public async Task Import()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(MainWindow.mainwindow);
            if (topLevel?.StorageProvider is { } storageProvider && storageProvider.CanOpen)
            {
                var mrpackFileType = new FilePickerFileType("Modrinth整合包文件")
                {
                    Patterns = new[] { "*.mrpack" },
                    MimeTypes = new[] { "application/mrpack" }
                };

                var options = new FilePickerOpenOptions
                {
                    Title = "选择 Modrinth Pack 文件",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { mrpackFileType },
                };
                var files = await storageProvider.OpenFilePickerAsync(options);
                var selectedFile = files.FirstOrDefault();

                if (files == null || !files.Any() || selectedFile == null)
                    return;

                string filePath = selectedFile.Path.LocalPath;
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("正在导入。。。（请打开任务中心查看进度）"));
                var task = _unitedCore.TaskManager.CreateTask("整合包导入");
                await ModpackImporter.ImportFromMrpackAsync(filePath,
                    Init.GameRootPath,
                    new Progress<(DownProgress a, int b, int c, string d)>
                    (p =>
                    {
                        double progressPercentage = (p.b == 0) ? 0 : (double)p.c / p.b * 100;
                        task.SetProgress($"当前文件：{p.d}", progressPercentage);
                        Debug.WriteLine($"导入进度：{p.a}, 总文件数：{p.b}, 已下载文件数：{p.c}, 当前文件：{p.d}");
                    })
                    , task!.Token!.Token);
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("导入完成！"));
                task.SetDone();
            }
        }
        catch(OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex, () => _ = Import());
        }
        catch (Exception ex)
        {
            await OlanExceptionWorker.ForUnknowException(ex, () => _ = Import());
        }
    }
    #endregion
    #region 新闻栏目
    private Task InitializeNewsAsync()
        =>UpdateNewsAsync(() => _newsReader.GetCurrentNewsAsync());
    
    [RelayCommand] private Task NextNews()
        => UpdateNewsAsync(() => _newsReader.GetNextNewsAsync());

    [RelayCommand] private Task PreviousNews()
        =>UpdateNewsAsync(() => _newsReader.GetPreviousNewsAsync());
    

    [RelayCommand]
    private void ViewDetails()
    {
        if (string.IsNullOrEmpty(CurrentNews.DetailsUrl)) return;

        try
        {
            Process.Start(new ProcessStartInfo(CurrentNews.DetailsUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"无法打开链接: {CurrentNews.DetailsUrl}", NotificationType.Error));
        }
    }

    private async Task UpdateNewsAsync(Func<Task<MinecraftNew>> getNewsTask)
    {
        IsNewsLoading = true;
        try
        {
            CurrentNews = await getNewsTask.Invoke();
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"获取新闻失败，请检查网络连接{Environment.NewLine}{ex.Message}", NotificationType.Error));
        }
        finally
        {
            IsNewsLoading = false;
        }
    }
    #endregion
}
