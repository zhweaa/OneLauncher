using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Server;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;
using OneLauncher.Views.Converters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using OneLauncher.Interfaces;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OneLauncher.ViewModel.Page;

namespace OneLauncher.Views.ViewModels;
internal sealed class IdentifierVisibilityChangedMessage { }

internal partial class SettingsPageViewModel : BaseViewModel, IPageHost, IListTemplate<NavigationVisibilityItem>
{
    // 将 manager 重命名为 _dbManger 以遵循常见的私有字段命名约定
    private readonly DBManager _dbManger;
    private JavaManager _javaManager => _unitedCore.JavaManager;
    private GameDataManager _gameDataManager => _unitedCore.GameDataManager;
    private readonly UnitedCore _unitedCore;
    public string Version => Init.ApplicationVersoin;

    #region 设置筛选
    public string[] SettingsCategories { get; } = ["全部设置", "界面", "游戏启动", "下载", "运行环境"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInterfaceSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsGameLaunchSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsDownloadSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsRuntimeSectionVisible))]
    private string selectedSettingsCategory = "全部设置";

    public bool IsInterfaceSectionVisible => SelectedSettingsCategory is "全部设置" or "界面";
    public bool IsGameLaunchSectionVisible => SelectedSettingsCategory is "全部设置" or "游戏启动";
    public bool IsDownloadSectionVisible => SelectedSettingsCategory is "全部设置" or "下载";
    public bool IsRuntimeSectionVisible => SelectedSettingsCategory is "全部设置" or "运行环境";
    #endregion

    #region 启动参数优化选项
    [ObservableProperty]
    public bool isM1, isM2, isM3;
    partial void OnIsM1Changed(bool value)
    {
        if (!value) return; // 避免在取消选中时也执行
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.DefaultSettings.MinecraftJvmArguments = JvmArguments.CreateFromMode(OptimizationMode.Conservative);
        _dbManger.Save();
    }
    partial void OnIsM2Changed(bool value)
    {
        if (!value) return;
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.DefaultSettings.MinecraftJvmArguments = JvmArguments.CreateFromMode(OptimizationMode.Standard);
        _dbManger.Save();
    }
    partial void OnIsM3Changed(bool value)
    {
        if (!value) return;
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.DefaultSettings.MinecraftJvmArguments = JvmArguments.CreateFromMode(OptimizationMode.Aggressive);
        _dbManger.Save();
    }
    [RelayCommand]
    private void ReleaseMemory()
    {
        Process.Start(new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = Environment.ProcessPath,
            Arguments = "--releaseMemory"
#if ONELAUNCHER_TARGET_WINDOWS
            ,
            Verb = "runas" // 请求管理员权限
#endif
        });
    }
    #endregion

    #region UI选项
    [ObservableProperty]
    private bool showIdentifiers = true;

    [ObservableProperty]
    private bool isSavingIdentifiers;

    partial void OnShowIdentifiersChanged(bool value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        _ = SaveIdentifierVisibilityAsync(value);
    }

    private async Task SaveIdentifierVisibilityAsync(bool value)
    {
        var settings = _dbManger.Data.OlanSettings;
        bool previous = settings.ShowIdentifiers;
        IsSavingIdentifiers = true;
        try
        {
            settings.ShowIdentifiers = value;
            await _dbManger.Save();
            WeakReferenceMessenger.Default.Send(new IdentifierVisibilityChangedMessage());
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                "标识符显示设置已保存并应用", NotificationType.Success));
        }
        catch (Exception ex)
        {
            settings.ShowIdentifiers = previous;
            SetProperty(ref showIdentifiers, previous, nameof(ShowIdentifiers));
            if (ex is OlanException olanException)
                await OlanExceptionWorker.ForOlanException(olanException);
            else
                await OlanExceptionWorker.ForUnknowException(ex);
        }
        finally
        {
            IsSavingIdentifiers = false;
        }
    }

    [ObservableProperty]
    public int _leftButtonListSpacing;
    partial void OnLeftButtonListSpacingChanged(int value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        _dbManger.Data.OlanSettings.LeftButtonListSpacing = Math.Clamp(value, 0, 30);
        _dbManger.Save();
    }

    [ObservableProperty]
    private ObservableCollection<NavigationVisibilityItem> items = new();

    [ObservableProperty]
    private bool isNavigationEditing;

    [ObservableProperty]
    private bool isSavingNavigation;

    partial void OnIsNavigationEditingChanged(bool value)
    {
        if (value && !IsSavingNavigation)
            _ = RefList();
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

    private void RebuildList()
    {
#if DEBUG
        var settings = Design.IsDesignMode ? new AppSettings() : _dbManger.Data.OlanSettings;
#else
        var settings = _dbManger.Data.OlanSettings;
#endif
        Items = new ObservableCollection<NavigationVisibilityItem>(CreateNavigationItems(settings));
    }

    private static List<NavigationVisibilityItem> CreateNavigationItems(AppSettings settings) =>
    [
        new(NavigationPage.Version, "版本管理", settings.IsVersionPageVisible),
        new(NavigationPage.GameData, "游戏数据", settings.IsGameDataPageVisible),
        new(NavigationPage.Server, "服务器收藏", settings.IsServerPageVisible),
        new(NavigationPage.Download, "下载管理", settings.IsDownloadPageVisible),
        new(NavigationPage.ModsBrowser, "资源检索", settings.IsModsBrowserPageVisible),
        new(NavigationPage.Account, "账户管理", settings.IsAccountPageVisible)
    ];

    private static void ApplyNavigationItems(AppSettings settings, IEnumerable<NavigationVisibilityItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Page)
            {
                case NavigationPage.Version: settings.IsVersionPageVisible = item.IsVisible; break;
                case NavigationPage.GameData: settings.IsGameDataPageVisible = item.IsVisible; break;
                case NavigationPage.Server: settings.IsServerPageVisible = item.IsVisible; break;
                case NavigationPage.Download: settings.IsDownloadPageVisible = item.IsVisible; break;
                case NavigationPage.ModsBrowser: settings.IsModsBrowserPageVisible = item.IsVisible; break;
                case NavigationPage.Account: settings.IsAccountPageVisible = item.IsVisible; break;
            }
        }
    }

    [RelayCommand]
    private async Task SaveNavigation()
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        var settings = _dbManger.Data.OlanSettings;
        var previousItems = CreateNavigationItems(settings);
        ServerEntry? addedServer = null;
        IsSavingNavigation = true;
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("正在保存导航栏设置"));
        try
        {
            ApplyNavigationItems(settings, Items);
            // 默认服务器仅在保存时添加；没有游戏实例时保持服务器列表为空。
            if (settings.IsServerPageVisible && _dbManger.Data.ServerList.Count == 0 &&
                _gameDataManager.GetDefaultInstance() is { } defaultInstance)
            {
                addedServer = new(Guid.NewGuid(), defaultInstance.InstanceId,
                    new() { Ip = "mc.hypixel.net", Port = 25565 }, "Hypixel",
                    "为避免空旷为您添加了一个默认的受欢迎服务器");
                _dbManger.Data.ServerList.Add(addedServer);
            }

            await _dbManger.Save();
            IsNavigationEditing = false;
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                "导航栏设置已保存并应用", NotificationType.Success));
        }
        catch (Exception ex)
        {
            ApplyNavigationItems(settings, previousItems);
            if (addedServer != null)
                _dbManger.Data.ServerList.Remove(addedServer);
            if (ex is OlanException olanException)
                await OlanExceptionWorker.ForOlanException(olanException);
            else
                await OlanExceptionWorker.ForUnknowException(ex);
        }
        finally
        {
            IsSavingNavigation = false;
        }
    }
    #endregion

    #region 主页样式
    public string[] HomePageStyles { get; } = ["默认", "魔法按钮"];
    private bool _isLoadingHomeSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMagicHomeOptionsVisible))]
    private string selectedHomePageStyle = "默认";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HomeBackgroundDisplayName))]
    [NotifyPropertyChangedFor(nameof(HasHomeBackground))]
    private string? homeBackgroundImagePath;

    [ObservableProperty]
    private bool isSavingHomeAppearance;

    [ObservableProperty]
    private ObservableCollection<HomeSectionVisibilityItem> homeSectionItems = new();

    [ObservableProperty]
    private bool isHomeSectionEditing;

    [ObservableProperty]
    private bool isSavingHomeSections;

    [ObservableProperty]
    private ObservableCollection<IQuicklyLaunchItem> homeLaunchItems = new();

    [ObservableProperty]
    private IQuicklyLaunchItem? selectedHomeLaunchItem;

    [ObservableProperty]
    private bool isSavingHomeLaunchItem;

    private string? _homeLaunchItemsSignature;

    public bool IsMagicHomeOptionsVisible => SelectedHomePageStyle == "魔法按钮";
    public bool HasHomeBackground => !string.IsNullOrWhiteSpace(HomeBackgroundImagePath);
    public string HomeBackgroundDisplayName => HasHomeBackground ? Path.GetFileName(HomeBackgroundImagePath)! : "未设置背景";

    partial void OnSelectedHomePageStyleChanged(string value)
    {
        if (_isLoadingHomeSettings)
            return;
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        var style = value == "魔法按钮" ? HomePageStyle.MagicButton : HomePageStyle.Classic;
        _ = SaveHomeAppearanceAsync(settings =>
        {
            settings.HomePageStyle = style;
            // 配置中的背景丢失（如被旧版本重写）或文件已不存在时，使用 customdata 中已复制的背景。
            if (style == HomePageStyle.MagicButton && !File.Exists(settings.HomeBackgroundImagePath))
                settings.HomeBackgroundImagePath = HomeBackgroundStore.FindLatest();
        }, "主页样式已保存并应用");
    }

    partial void OnIsHomeSectionEditingChanged(bool value)
    {
        if (value && !IsSavingHomeSections)
            RebuildHomeSectionItems();
    }

    partial void OnSelectedHomeLaunchItemChanged(IQuicklyLaunchItem? value)
    {
        if (_isLoadingHomeSettings || value == null)
            return;
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        _ = SaveHomeLaunchItemAsync(value);
    }

    private async Task SaveHomeLaunchItemAsync(IQuicklyLaunchItem item)
    {
        var settings = _dbManger.Data.OlanSettings;
        var previousInstance = settings.DefaultInstanceID;
        var previousServer = settings.HomeDefaultServerID;
        IsSavingHomeLaunchItem = true;
        try
        {
            QuicklyLaunchItems.SaveAsDefault(settings, item);
            await _dbManger.Save();
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                $"主页默认启动项已设为：{item.Name}", NotificationType.Success));
        }
        catch (Exception ex)
        {
            settings.DefaultInstanceID = previousInstance;
            settings.HomeDefaultServerID = previousServer;
            RefreshHomeLaunchItems(force: true);
            await ShowSettingsException(ex);
        }
        finally
        {
            IsSavingHomeLaunchItem = false;
        }
    }

    // 游戏数据或服务器收藏变化后同步下拉列表，并重新对齐当前默认项。
    private void RefreshHomeLaunchItems(bool force = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RefreshHomeLaunchItems(force));
            return;
        }
        _isLoadingHomeSettings = true;
        try
        {
            var signature = QuicklyLaunchItems.Signature(_unitedCore, _dbManger);
            if (force || signature != _homeLaunchItemsSignature)
            {
                _homeLaunchItemsSignature = signature;
                HomeLaunchItems = new ObservableCollection<IQuicklyLaunchItem>(
                    QuicklyLaunchItems.Create(_unitedCore, _dbManger));
            }
            SelectedHomeLaunchItem = QuicklyLaunchItems.ResolveDefault(HomeLaunchItems, _dbManger.Data.OlanSettings);
        }
        finally
        {
            _isLoadingHomeSettings = false;
        }
    }

    private void LoadHomeSettings(AppSettings settings)
    {
        _isLoadingHomeSettings = true;
        try
        {
            SelectedHomePageStyle = settings.HomePageStyle == HomePageStyle.MagicButton ? "魔法按钮" : "默认";
            HomeBackgroundImagePath = settings.HomeBackgroundImagePath;
        }
        finally
        {
            _isLoadingHomeSettings = false;
        }
        RebuildHomeSectionItems();
    }

    private void RebuildHomeSectionItems()
    {
#if DEBUG
        var settings = Design.IsDesignMode ? new AppSettings() : _dbManger.Data.OlanSettings;
#else
        var settings = _dbManger.Data.OlanSettings;
#endif
        HomeSectionItems = new ObservableCollection<HomeSectionVisibilityItem>(CreateHomeSectionItems(settings));
    }

    private static List<HomeSectionVisibilityItem> CreateHomeSectionItems(AppSettings settings) =>
    [
        new(HomeSection.News, "MC新闻", settings.IsHomeNewsVisible),
        new(HomeSection.TaskCenter, "任务中心", settings.IsHomeTaskCenterVisible),
        new(HomeSection.PowerPlay, "联机面板", settings.IsHomePowerPlayVisible),
        new(HomeSection.Import, "导入游戏实例", settings.IsHomeImportVisible)
    ];

    private static void ApplyHomeSectionItems(AppSettings settings, IEnumerable<HomeSectionVisibilityItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Section)
            {
                case HomeSection.News: settings.IsHomeNewsVisible = item.IsVisible; break;
                case HomeSection.TaskCenter: settings.IsHomeTaskCenterVisible = item.IsVisible; break;
                case HomeSection.PowerPlay: settings.IsHomePowerPlayVisible = item.IsVisible; break;
                case HomeSection.Import: settings.IsHomeImportVisible = item.IsVisible; break;
            }
        }
    }

    [RelayCommand]
    private async Task SaveHomeSections()
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        var settings = _dbManger.Data.OlanSettings;
        var previousItems = CreateHomeSectionItems(settings);
        IsSavingHomeSections = true;
        try
        {
            ApplyHomeSectionItems(settings, HomeSectionItems);
            await _dbManger.Save();
            IsHomeSectionEditing = false;
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                "主页板块设置已保存并应用", NotificationType.Success));
        }
        catch (Exception ex)
        {
            ApplyHomeSectionItems(settings, previousItems);
            await ShowSettingsException(ex);
        }
        finally
        {
            IsSavingHomeSections = false;
        }
    }

    [RelayCommand]
    private async Task ChooseHomeBackground()
    {
        if (IsSavingHomeAppearance)
            return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(MainWindow.mainwindow);
            if (topLevel?.StorageProvider is not { CanOpen: true } storageProvider)
                return;
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择主页背景图片",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }]
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
                return;
            var previous = _dbManger.Data.OlanSettings.HomeBackgroundImagePath;
            // 复制到 customdata 后再保存，原图被移动或删除也不影响主页背景。
            var copied = await HomeBackgroundStore.ImportAsync(path);
            if (await SaveHomeAppearanceAsync(settings => settings.HomeBackgroundImagePath = copied, "主页背景已保存并应用"))
                HomeBackgroundStore.TryDelete(previous);
            else
                HomeBackgroundStore.TryDelete(copied);
        }
        catch (Exception ex)
        {
            await ShowSettingsException(ex);
        }
    }

    [RelayCommand]
    private async Task ClearHomeBackground()
    {
        if (await SaveHomeAppearanceAsync(settings => settings.HomeBackgroundImagePath = null, "已移除主页背景"))
            HomeBackgroundStore.TryDeleteAll();
    }

    private async Task<bool> SaveHomeAppearanceAsync(Action<AppSettings> apply, string successMessage)
    {
        var settings = _dbManger.Data.OlanSettings;
        var previousStyle = settings.HomePageStyle;
        var previousBackground = settings.HomeBackgroundImagePath;
        IsSavingHomeAppearance = true;
        try
        {
            apply(settings);
            await _dbManger.Save();
            HomeBackgroundImagePath = settings.HomeBackgroundImagePath;
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(successMessage, NotificationType.Success));
            return true;
        }
        catch (Exception ex)
        {
            settings.HomePageStyle = previousStyle;
            settings.HomeBackgroundImagePath = previousBackground;
            LoadHomeSettings(settings);
            await ShowSettingsException(ex);
            return false;
        }
        finally
        {
            IsSavingHomeAppearance = false;
        }
    }

    private static Task ShowSettingsException(Exception ex)
        => ex is OlanException olanException
            ? OlanExceptionWorker.ForOlanException(olanException)
            : OlanExceptionWorker.ForUnknowException(ex);
    #endregion

    #region 下载选项
    [ObservableProperty]
    public int _MaxDownloadThreadsValue;
    partial void OnMaxDownloadThreadsValueChanged(int value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.MaximumDownloadThreads = value;
        _dbManger.Save();
    }

    [ObservableProperty]
    public int _MaxSha1ThreadsValue;
    partial void OnMaxSha1ThreadsValueChanged(int value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.MaximumSha1Threads = value;
        _dbManger.Save();
    }

    [ObservableProperty]
    public bool _IsSha1Enabled;
    partial void OnIsSha1EnabledChanged(bool value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.IsSha1Enabled = value;
        _dbManger.Save();
    }

    [ObservableProperty]
    public DownloadSourceStrategy[] downloadSources =
        [
        DownloadSourceStrategy.OfficialOnly,
        DownloadSourceStrategy.RaceWithBmcl,
        DownloadSourceStrategy.RaceWithOlan
        ];
    [ObservableProperty]
    public DownloadSourceStrategy _SelectedDownloadSource = DownloadSourceStrategy.OfficialOnly;
    partial void OnSelectedDownloadSourceChanged(DownloadSourceStrategy value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        _dbManger.Data.OlanSettings.DownloadMinecraftSourceStrategy = value;
        _dbManger.Save();
    }

    #endregion
    [RelayCommand]
    public void OpenGithub()
    {
        Tools.OpenWebsite(Init.ProjectWebsite);
    }
    [ObservableProperty]
    UserControl? _paneContent;
    [ObservableProperty] bool _isPaneShow;

    [RelayCommand]
    private void OpenJavaInstallPane()
    {
        PaneContent = new JavaInstallPane
        {
            DataContext = new JavaInstallPaneViewModel(_javaManager, _unitedCore, () => IsPaneShow = false)
        };
        IsPaneShow = true;
    }

    // 构造函数接收正确的 DBManger 类型
    public SettingsPageViewModel(DBManager configManager,UnitedCore unitedCore)
    {
        this._dbManger = configManager;
        this._unitedCore = unitedCore;
#if DEBUG
        if (Design.IsDesignMode)
        {
            LeftButtonListSpacing = 8;
            _ = RefList();
            LoadHomeSettings(new AppSettings());
            MaxDownloadThreadsValue = 24;
            MaxSha1ThreadsValue = 24;
            IsSha1Enabled = true;
            return; // 在设计模式下提前返回
        }
#endif
        // else 块不再需要，因为非 DEBUG 模式下总会执行下面的代码

        try
        {
            // 使用注入的实例来初始化属性
            showIdentifiers = _dbManger.Data.OlanSettings.ShowIdentifiers;
            LeftButtonListSpacing = _dbManger.Data.OlanSettings.LeftButtonListSpacing;
            _ = RefList();
            LoadHomeSettings(_dbManger.Data.OlanSettings);
            RefreshHomeLaunchItems();
            _dbManger.OnDataChanged += () => RefreshHomeLaunchItems();
            _gameDataManager.OnDataChanged += () => RefreshHomeLaunchItems();
            switch (_dbManger.Data.DefaultSettings.MinecraftJvmArguments.mode)
            {
                case OptimizationMode.Conservative:
                    IsM1 = true;
                    break;
                case OptimizationMode.Standard:
                    IsM2 = true;
                    break;
                case OptimizationMode.Aggressive:
                    IsM3 = true;
                    break;
            }
            MaxDownloadThreadsValue = _dbManger.Data.OlanSettings.MaximumDownloadThreads;
            MaxSha1ThreadsValue = _dbManger.Data.OlanSettings.MaximumSha1Threads;
            IsSha1Enabled = _dbManger.Data.OlanSettings.IsSha1Enabled;
            SelectedDownloadSource = _dbManger.Data.OlanSettings.DownloadMinecraftSourceStrategy;
        }
        catch (NullReferenceException ex)
        {
            // 异常处理中的静态调用暂时保留，因为这是更深层次的重构问题
            throw new OlanException(
                "内部异常",
                "配置文件特定部分设置部分为空，这可能是新版和旧版配置文件不兼容导致的",
                OlanExceptionAction.FatalError,
                ex,
               () =>
               {
                   File.Delete(Path.Combine(Init.BasePath, "config.json"));
                   Init.Initialize(); // 注意：这个静态调用最终也应被移除
               }
            );
        }
    }
}
