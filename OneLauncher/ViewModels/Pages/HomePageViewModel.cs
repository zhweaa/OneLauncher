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
using OneLauncher.Core.DefaultComponent.Server;

namespace OneLauncher.Views.ViewModels;

#region 快速启动项
/// <summary>主页快速启动项。游戏数据和服务器收藏共用同一个列表，新增启动来源时实现该接口即可。</summary>
internal interface IQuicklyLaunchItem
{
    string Name { get; }
    string Description { get; }
    Bitmap Icon { get; }
    Task Launch();
}

internal sealed class GameDataQuicklyLaunchItem(UnitedCore unitedCore, GameData gameData) : IQuicklyLaunchItem
{
    public GameData GameData { get; } = gameData;
    public string Name => GameData.Name;
    public string Description => $"游戏数据 · {GameData.VersionId}";
    public Bitmap Icon { get; } = GameDataItem.GetGameDataIcon(gameData);
    public Task Launch() => Game.EasyGameLauncher(unitedCore, GameData, null, false);
}

internal sealed class ServerQuicklyLaunchItem(UnitedCore unitedCore, ServerEntry server) : IQuicklyLaunchItem
{
    public ServerEntry Server { get; } = server;
    public string Name => Server.Name;
    public string Description => $"服务器收藏 · {Server.ServerInfo.Ip}:{Server.ServerInfo.Port}";
    public Bitmap Icon { get; } = ServerIconLoader.Load(server);

    public async Task Launch()
    {
        GameData? gameData = unitedCore.GameDataManager.GetInstanceFromId(Server.InstanceId);
        if (gameData == null)
        {
            await OlanExceptionWorker.ForOlanException(new OlanException(
                "启动失败", $"没有找到服务器对应的游戏实例：{Server.InstanceId}", OlanExceptionAction.Error));
            return;
        }
        await Game.EasyGameLauncher(unitedCore, gameData, Server.ServerInfo, false);
    }
}

/// <summary>主页与设置页共用的快速启动列表构建和默认项读写。</summary>
internal static class QuicklyLaunchItems
{
    public static List<IQuicklyLaunchItem> Create(UnitedCore core, DBManager config) =>
    [
        .. core.GameDataManager.AllGameData.Select(g => (IQuicklyLaunchItem)new GameDataQuicklyLaunchItem(core, g)),
        .. config.Data.ServerList.Select(s => (IQuicklyLaunchItem)new ServerQuicklyLaunchItem(core, s))
    ];

    // 只在显示内容变化时重建列表，避免每次保存配置都重新加载图标并打断下拉框选择。
    public static string Signature(UnitedCore core, DBManager config) => string.Join("\n",
        core.GameDataManager.AllGameData.Select(g => $"game:{g.InstanceId}|{g.Name}|{g.VersionId}|{g.ModLoader}")
            .Concat(config.Data.ServerList.Select(s =>
                $"server:{s.Id}|{s.Name}|{s.InstanceId}|{s.ServerInfo.Ip}|{s.ServerInfo.Port}")));

    public static string? KeyOf(IQuicklyLaunchItem? item) => item switch
    {
        GameDataQuicklyLaunchItem game => $"game:{game.GameData.InstanceId}",
        ServerQuicklyLaunchItem server => $"server:{server.Server.Id}",
        _ => null
    };

    public static IQuicklyLaunchItem? Find(IEnumerable<IQuicklyLaunchItem> items, string? key)
        => key == null ? null : items.FirstOrDefault(i => KeyOf(i) == key);

    // 服务器默认项失效（如收藏已删除）时回退到默认游戏数据。
    public static IQuicklyLaunchItem? ResolveDefault(IEnumerable<IQuicklyLaunchItem> items, AppSettings settings)
        => (settings.HomeDefaultServerID is { } serverId ? Find(items, $"server:{serverId}") : null)
           ?? (settings.DefaultInstanceID is { } instanceId ? Find(items, $"game:{instanceId}") : null);

    public static void SaveAsDefault(AppSettings settings, IQuicklyLaunchItem item)
    {
        switch (item)
        {
            case GameDataQuicklyLaunchItem game:
                settings.DefaultInstanceID = game.GameData.InstanceId;
                settings.HomeDefaultServerID = null;
                break;
            case ServerQuicklyLaunchItem server:
                settings.HomeDefaultServerID = server.Server.Id;
                break;
        }
    }
}
#endregion

internal partial class HomePageViewModel : BaseViewModel, IPageHost, IListTemplate<IQuicklyLaunchItem>
{
    private GameDataManager _gameDataManager => _unitedCore.GameDataManager;
    private DBManager _configManager;
    private readonly UnitedCore _unitedCore;
    private readonly PowerPlayPaneViewModelFactory _mctVMFactory;
    private readonly TaskCenterPaneViewModelFactory _taskCenterVMFactory;
    private readonly INewReader _newsReader;

    [ObservableProperty] private ObservableCollection<IQuicklyLaunchItem> items = new();
    [ObservableProperty]
    private IQuicklyLaunchItem? selectedLaunchItem = null;
    // 设置页保存的主页默认启动项；魔法按钮样式不提供选择，只启动该项。
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MagicLaunchTitle))]
    [NotifyPropertyChangedFor(nameof(MagicLaunchDescription))]
    private IQuicklyLaunchItem? defaultLaunchItem;
    [ObservableProperty] private Bitmap gameIcon;
    private string? _launchItemsSignature;
    private string? _appliedDefaultKey;

    [ObservableProperty] private bool isPaneShow;
    [ObservableProperty] private UserControl? paneContent;

    [ObservableProperty] private bool _isNewsLoading;
    [ObservableProperty] private MinecraftNew _currentNews;

    #region 主页样式
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClassicStyle))]
    [NotifyPropertyChangedFor(nameof(IsNewsSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsTaskCenterSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsPowerPlaySectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsImportSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsHomeSectionsVisible))]
    [NotifyPropertyChangedFor(nameof(HasHomeBackground))]
    private bool isMagicButtonStyle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsHomeSectionsVisible))]
    private bool isNewsEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTaskCenterSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsHomeSectionsVisible))]
    private bool isTaskCenterEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPowerPlaySectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsHomeSectionsVisible))]
    private bool isPowerPlayEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsHomeSectionsVisible))]
    private bool isImportEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHomeBackground))]
    private Bitmap? homeBackground;

    private string? _loadedBackgroundPath;

    public bool IsClassicStyle => !IsMagicButtonStyle;
    // 默认样式保持原有的完整主页，板块开关只作用于魔法按钮样式。
    public bool IsNewsSectionVisible => !IsMagicButtonStyle || IsNewsEnabled;
    public bool IsTaskCenterSectionVisible => !IsMagicButtonStyle || IsTaskCenterEnabled;
    public bool IsPowerPlaySectionVisible => !IsMagicButtonStyle || IsPowerPlayEnabled;
    public bool IsImportSectionVisible => !IsMagicButtonStyle || IsImportEnabled;
    public bool IsHomeSectionsVisible => IsNewsSectionVisible || IsTaskCenterSectionVisible ||
        IsPowerPlaySectionVisible || IsImportSectionVisible;
    public bool HasHomeBackground => IsMagicButtonStyle && HomeBackground != null;
    public string MagicLaunchTitle => DefaultLaunchItem?.Name ?? "开始游戏";
    public string MagicLaunchDescription => DefaultLaunchItem?.Description ?? "请在设置中选择主页默认启动项";

    private void OnConfigDataChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyHomeSettings();
        else
            Dispatcher.UIThread.Post(ApplyHomeSettings);
    }

    private void ApplyHomeSettings()
    {
        var settings = _configManager.Data.OlanSettings;
        RefreshLaunchItems();
        IsMagicButtonStyle = settings.HomePageStyle == HomePageStyle.MagicButton;
        IsNewsEnabled = settings.IsHomeNewsVisible;
        IsTaskCenterEnabled = settings.IsHomeTaskCenterVisible;
        IsPowerPlayEnabled = settings.IsHomePowerPlayVisible;
        IsImportEnabled = settings.IsHomeImportVisible;
        // 默认样式不显示背景，也不占用背景图内存。
        LoadHomeBackground(IsMagicButtonStyle ? settings.HomeBackgroundImagePath : null);
    }

    private void LoadHomeBackground(string? path)
    {
        if (string.Equals(path, _loadedBackgroundPath, StringComparison.OrdinalIgnoreCase) &&
            (path == null || HomeBackground != null))
            return;

        var previous = HomeBackground;
        _loadedBackgroundPath = path;
        HomeBackground = null;
        previous?.Dispose();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var bitmap = new Bitmap(path);
            // 超大壁纸缩小后保存，避免长期占用过多内存；普通图片保持原尺寸。
            if (bitmap.PixelSize.Width > 2560)
            {
                var height = Math.Max(1, (int)Math.Round(bitmap.PixelSize.Height * 2560d / bitmap.PixelSize.Width));
                var scaled = bitmap.CreateScaledBitmap(new Avalonia.PixelSize(2560, height), BitmapInterpolationMode.HighQuality);
                bitmap.Dispose();
                bitmap = scaled;
            }
            HomeBackground = bitmap;
        }
        catch (Exception ex)
        {
            // 背景图丢失或损坏时回退到无背景，不影响主页使用。
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                $"无法加载主页背景图片，已暂时使用无背景样式{Environment.NewLine}{ex.Message}", NotificationType.Warning));
        }
    }
    #endregion
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
        _gameDataManager.OnDataChanged += OnGameDataChanged;
        // 构建快速启动列表并选中设置中的默认启动项。
        ApplyHomeSettings();
        _configManager.OnDataChanged += OnConfigDataChanged;
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

    private void RebuildList() => RefreshLaunchItems();

    private void OnGameDataChanged() => _ = RefList();

    private void RefreshLaunchItems()
    {
        var signature = QuicklyLaunchItems.Signature(_unitedCore, _configManager);
        if (signature != _launchItemsSignature)
        {
            var selectedKey = QuicklyLaunchItems.KeyOf(SelectedLaunchItem);
            _launchItemsSignature = signature;
            Items = new ObservableCollection<IQuicklyLaunchItem>(QuicklyLaunchItems.Create(_unitedCore, _configManager));
            SelectedLaunchItem = QuicklyLaunchItems.Find(Items, selectedKey);
        }

        DefaultLaunchItem = QuicklyLaunchItems.ResolveDefault(Items, _configManager.Data.OlanSettings);
        var defaultKey = QuicklyLaunchItems.KeyOf(DefaultLaunchItem);
        // 默认项被设置页修改时，默认样式的下拉框也跟随；用户在主页的临时选择不受其他配置保存影响。
        if (SelectedLaunchItem == null || defaultKey != _appliedDefaultKey)
            SelectedLaunchItem = DefaultLaunchItem;
        _appliedDefaultKey = defaultKey;
        GameIcon ??= GameDataItem.GetGameDataIcon(null);
    }

    partial void OnSelectedLaunchItemChanged(IQuicklyLaunchItem? value)
    {
        GameIcon = value?.Icon ?? GameDataItem.GetGameDataIcon(null);
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
        if(SelectedLaunchItem is not { } item)
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("请选择一个启动项！", Avalonia.Controls.Notifications.NotificationType.Warning));
            return;
        }
        // 与原逻辑一致：从默认样式启动后，该项成为下次的默认启动项。
        QuicklyLaunchItems.SaveAsDefault(_configManager.Data.OlanSettings, item);
        _ = SaveConfigSafelyAsync();
        _ = item.Launch();
    }

    [RelayCommand]
    private void LaunchDefault()
    {
        if (DefaultLaunchItem is not { } item)
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                "尚未设置主页默认启动项，请在设置中选择", NotificationType.Warning));
            return;
        }
        _ = item.Launch();
    }

    private async Task SaveConfigSafelyAsync()
    {
        try { await _configManager.Save(); }
        catch (OlanException ex) { await OlanExceptionWorker.ForOlanException(ex); }
        catch (Exception ex) { await OlanExceptionWorker.ForUnknowException(ex); }
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
