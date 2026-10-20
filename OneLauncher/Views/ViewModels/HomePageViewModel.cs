using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.ImportPCL2Version;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using OneLauncher.Core.Mod.ModPack;
using OneLauncher.Core.Services;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;
using OneLauncher.Views.Panes.PaneViewModels.Factories;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Views.ViewModels;

internal partial class HomePageViewModel : BaseViewModel
{
    private readonly GameDataManager _gameDataManager;
    private readonly DBManager _configManager;
    private readonly UnitedCore _unitedCore;
    private readonly PowerPlayPaneViewModelFactory _mctVMFactory;
    private readonly TaskCenterPaneViewModelFactory _taskCenterVMFactory;
    private readonly INewReader _newsReader;
    private TaskCenterPaneViewModel? _taskCenterPane;

    [ObservableProperty] private List<GameData> launchItems;
    [ObservableProperty] private GameData? selectedGameData = null;
    [ObservableProperty] private Bitmap gameIcon;

    [ObservableProperty] private bool isPaneShow;
    [ObservableProperty] private UserControl paneContent;

    [ObservableProperty] private bool _isNewsLoading;
    [ObservableProperty] private MinecraftNew _currentNews;
    public HomePageViewModel(
        GameDataManager gameDataManager,
        DBManager configManager,
        UnitedCore unitedCore,
        PowerPlayPaneViewModelFactory powerPlayPaneViewModelFactory,
        TaskCenterPaneViewModelFactory taskCenterPaneViewModelFactory
        )
    {
        this._configManager = configManager;
        this._gameDataManager = gameDataManager;
        this._unitedCore = unitedCore;
        this._mctVMFactory = powerPlayPaneViewModelFactory;
        this._taskCenterVMFactory = taskCenterPaneViewModelFactory;
        LaunchItems = _gameDataManager.AllGameData;
        _gameDataManager.OnDataChanged += () =>
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                LaunchItems = _gameDataManager.AllGameData;
            });
        };
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
    partial void OnSelectedGameDataChanged(GameData? value)
    {
        GameIcon = GameDataItem.GetGameDataIcon(value);
    }
    [RelayCommand]
    private void OpenTaskCenter()
    {
        _taskCenterPane ??= _taskCenterVMFactory.Create(() => IsPaneShow = false);
        PaneContent = new TaskCenterPane { DataContext = _taskCenterPane };
        IsPaneShow = true;
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
        TaskInstance? importTask = null;
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
                importTask = _unitedCore.TaskManager.CreateTask($"PCL 导入：{Path.GetFileName(path)}");
                OpenTaskCenter();
                await ImportTaskRunner.RunAsync(importTask,
                    (progress, token) => new PCL2Importer(progress, token).ImportAsync(path));
            }
        }
        catch (Exception ex)
        {
            importTask ??= _unitedCore.TaskManager.CreateTask("PCL 导入");
            importTask.SetFailed(ex);
            OpenTaskCenter();
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
        TaskInstance? importTask = null;
        try
        {
            var topLevel = TopLevel.GetTopLevel(MainWindow.mainwindow);
            if (topLevel?.StorageProvider is not { CanOpen: true } storageProvider)
                return;
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
            importTask = _unitedCore.TaskManager.CreateTask($"整合包导入：{Path.GetFileName(filePath)}");
            OpenTaskCenter();
            await ImportTaskRunner.RunAsync(importTask,
                (progress, token) => ModpackImporter.ImportFromMrpackAsync(filePath,
                    Init.GameRootPath, progress, token));
        }
        catch (Exception ex)
        {
            importTask ??= _unitedCore.TaskManager.CreateTask("整合包导入");
            importTask.SetFailed(ex);
            OpenTaskCenter();
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
