using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;
using OneLauncher.Views.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Interfaces;

namespace OneLauncher.Views;
internal class MainWindowShowFlyoutMessage
{
    public readonly string Title;
    public readonly string Context;
    public readonly NotificationType Type;
    public readonly bool OpenTaskCenterAfterClick;
    public MainWindowShowFlyoutMessage(string context, NotificationType type = NotificationType.Information,bool openTaskCenterAfterClick = false,string ? title = null)
    {
        this.Context = context;
        this.Type = type;
        this.OpenTaskCenterAfterClick = openTaskCenterAfterClick;
        // 以前的API没有标题，防止乱套给他个默认值
        this.Title = title ?? type switch
        {
            NotificationType.Information => "提示",
            NotificationType.Success => "成功",
            NotificationType.Warning => "警告",
            NotificationType.Error => "错误",
            _ => "通知"
        };
    }
}
public partial class MainWindow : Window
{
    public Home HomePage;
    public version versionPage;
    public download downloadPage;
    public settings settingsPage;
    public account accountPage;
    public ModsBrowser modsBrowserPage;
    public gamedata gamedataPage;
    public server serverPage;
    public static MainWindow mainwindow { get; set; }
    bool IsError;
    bool _isUiSettingsSubscribed;
    IServiceCollection servises;
    public readonly IServiceProvider provider;
    public MainWindow()
    {
        InitializeComponent();
        mainwindow = this;
        try
        {
            // 等待基本组件初始化完成，并在后续注册ViewModel
            servises = Init.InitTask.GetAwaiter().GetResult();

            servises.AddSingleton<AccountPageViewModel>();
            servises.AddSingleton<DownloadPageViewModel>();
            servises.AddSingleton<GameDataPageViewModel>();
            servises.AddSingleton<ServerPageViewModel>();
            servises.AddSingleton<HomePageViewModel>();
            servises.AddSingleton<ModsBrowserViewModel>();
            servises.AddSingleton<SettingsPageViewModel>();
            servises.AddSingleton<VersionPageViewModel>();

            // 工厂是单例，每次 Create 返回新的 Pane ViewModel 实例。
            servises.AddSingleton<NewGameDataPaneViewModelFactory>();
            servises.AddSingleton<AddServerPaneViewModelFactory>();
            servises.AddSingleton<EditServerPaneViewModelFactory>();
            servises.AddSingleton<DownloadPaneViewModelFactory>();
            servises.AddSingleton<EditGameDataPaneViewModelFactory>();
            servises.AddSingleton<PowerPlayPaneViewModelFactory>();
            servises.AddSingleton<TaskCenterPaneViewModelFactory>();
            servises.AddSingleton<InstallModPaneViewModelFactory>();
            servises.AddSingleton<InstallPackPaneViewModelFactory>();
            servises.AddSingleton<UserModelLoginPaneViewModelFactory>();

            provider = servises.BuildServiceProvider();
            PageContent.Content = new Home();
            ApplyUiSettings();
            Init.ConfigManger.OnDataChanged += OnConfigDataChanged;
            _isUiSettingsSubscribed = true;
            // 注册消息
            WeakReferenceMessenger.Default.Register<MainWindowShowFlyoutMessage>(this, (re, message) => ShowFlyout(message.Title,message.Context,message.Type,message.OpenTaskCenterAfterClick));
        }
        catch(OlanException e)
        {
            IsError = true;
            OlanExceptionWorker.ForOlanException(e);
        }
    }
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!IsError)
        {
            try
            {
                var homePage = (Home)PageContent.Content!;
                homePage.DataContext = provider.GetRequiredService<HomePageViewModel>();
                HomePage = homePage;
                versionPage = new version() 
                { DataContext = provider.GetRequiredService<VersionPageViewModel>() }; 
                accountPage = new account()
                { DataContext = provider.GetRequiredService<AccountPageViewModel>() };
                modsBrowserPage = new ModsBrowser()
                { DataContext = provider.GetRequiredService<ModsBrowserViewModel>() };
                downloadPage = new download()
                { DataContext = provider.GetRequiredService<DownloadPageViewModel>() };
                settingsPage = new settings()
                { DataContext = provider.GetRequiredService<SettingsPageViewModel>() };
                gamedataPage = new gamedata()
                { DataContext = provider.GetRequiredService<GameDataPageViewModel>() };
                serverPage = new server()
                { DataContext = provider.GetRequiredService<ServerPageViewModel>() };
            }
            catch (OlanException ex)
            {
                OlanExceptionWorker.ForOlanException(ex);
            }
        }
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_isUiSettingsSubscribed)
            Init.ConfigManger.OnDataChanged -= OnConfigDataChanged;
        foreach(var dis in Init.OnApplicationClosingReleaseSourcesList)
            dis.Dispose();
    }

    private void OnConfigDataChanged() => Dispatcher.UIThread.Post(ApplyUiSettings);

    private void ApplyUiSettings()
    {
        var appSettings = Init.ConfigManger.Data.OlanSettings;
        Resources["LeftButtonListItemMargin"] = new Thickness(
            Math.Clamp(appSettings.LeftButtonListSpacing, 0, 30));
        ServerListBoxItem.IsVisible = appSettings.IsServerPageVisible;
        VersionListBoxItem.IsVisible = appSettings.IsVersionPageVisible;
        GameDataListBoxItem.IsVisible = appSettings.IsGameDataPageVisible;
        DownloadListBoxItem.IsVisible = appSettings.IsDownloadPageVisible;
        ModsBrowserListBoxItem.IsVisible = appSettings.IsModsBrowserPageVisible;
        AccountListBoxItem.IsVisible = appSettings.IsAccountPageVisible;
        HomeListBoxItem.IsVisible = true;
        SettingsListBoxItem.IsVisible = true;

        if ((SplitListBox.SelectedItem is ListBoxItem selected && !selected.IsVisible) ||
            (versionPage != null && PageContent.Content == versionPage && !VersionListBoxItem.IsVisible) ||
            (gamedataPage != null && PageContent.Content == gamedataPage && !GameDataListBoxItem.IsVisible) ||
            (serverPage != null && PageContent.Content == serverPage && !ServerListBoxItem.IsVisible) ||
            (downloadPage != null && PageContent.Content == downloadPage && !DownloadListBoxItem.IsVisible) ||
            (modsBrowserPage != null && PageContent.Content == modsBrowserPage && !ModsBrowserListBoxItem.IsVisible) ||
            (accountPage != null && PageContent.Content == accountPage && !AccountListBoxItem.IsVisible))
        {
            if (HomePage != null)
            {
                SplitListBox.SelectedItem = HomeListBoxItem;
                PageContent.Content = HomePage;
            }
        }
    }

    /// <summary>
    /// 在右下角显示提示信息
    /// </summary>
    /// <param ID="text">提示信息内容</param>
    public void ShowFlyout(string title,string text,NotificationType type = NotificationType.Information,bool openTaskCenterAfterClick = false)
    {
        Action? onClick = openTaskCenterAfterClick ? OpenTaskCenterForCurrentPage : null;
        Dispatcher.UIThread.Post(() => NotificationManager.Show(
            new Notification(title, text, type, TimeSpan.FromSeconds(5), onClick)));
    }

    private void OpenTaskCenterForCurrentPage()
    {
        if (PageContent.Content is not Control { DataContext: IPageHost page })
            return;

        var pane = new TaskCenterPane();
        var closeCallback = new TaskCenterCloseCallback(page, pane);
        pane.DataContext = provider.GetRequiredService<TaskCenterPaneViewModelFactory>()
            .Create(closeCallback.Close);

        if (page.PaneContent?.DataContext is TaskCenterPaneViewModel previousTaskCenter)
            previousTaskCenter.Dispose();

        page.PaneContent = pane;
        page.IsPaneShow = true;
    }

    private sealed class TaskCenterCloseCallback(IPageHost page, TaskCenterPane pane)
    {
        public void Close()
        {
            if (ReferenceEquals(page.PaneContent, pane))
                page.IsPaneShow = false;
            if (pane.DataContext is TaskCenterPaneViewModel taskCenter)
                taskCenter.Dispose();
        }
    }
    
    // 统一事件方法
    private void ListBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox == null) return;

        var selectedItem = listBox.SelectedItem as ListBoxItem;
        if (selectedItem == null || !selectedItem.IsVisible) return;

        switch (selectedItem.Tag)
        {
            case "Home":
                PageContent.Content = HomePage;
                break;
            case "Version":
                PageContent.Content = versionPage;
                break;
            case "Account":
                PageContent.Content = accountPage;
                break;
            case "ModsBrowser":
                PageContent.Content = modsBrowserPage;
                break;
            case "Download":
                PageContent.Content = downloadPage;
                break;
            case "GameData":
                PageContent.Content = gamedataPage;
                break;
            case "Server":
                PageContent.Content = serverPage;
                break;
            case "Settings":
                PageContent.Content = settingsPage;
                break;
        }
        
    }
    // 切换侧边栏展开/折叠
    private void MangePaneOpenAndClose(bool isOpen) => SidebarSplitView.IsPaneOpen = isOpen;
    // 鼠标进入事件
    private void Sb_in(object? sender, Avalonia.Input.PointerEventArgs e) => MangePaneOpenAndClose(true);
    // 鼠标离开事件
    private void Sb_out(object? sender, Avalonia.Input.PointerEventArgs e) => MangePaneOpenAndClose(false);
}
