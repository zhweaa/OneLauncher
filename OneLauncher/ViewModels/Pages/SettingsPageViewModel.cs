using Avalonia.Controls;
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
