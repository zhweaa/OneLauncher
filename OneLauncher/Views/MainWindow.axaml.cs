using Avalonia.Controls;
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
using OneLauncher.Views.Panes.PaneViewModels.Factories;
using OneLauncher.Views.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OneLauncher.Views;
internal class MainWindowShowFlyoutMessage
{
    public readonly string Title;
    public readonly string Context;
    public readonly NotificationType Type;
    public MainWindowShowFlyoutMessage(string context, NotificationType type = NotificationType.Information,string ? title = null)
    {
        this.Context = context;
        this.Type = type;
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
    public static MainWindow mainwindow { get; set; }
    bool IsError;
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
            servises.AddSingleton<HomePageViewModel>();
            servises.AddSingleton<ModsBrowserViewModel>();
            servises.AddSingleton<SettingsPageViewModel>();
            servises.AddSingleton<VersionPageViewModel>();

            // Pane ViewModel本身是单例的，但工厂模式可以保证每次获取都是新的实例
            servises.AddSingleton<NewGameDataPaneViewModelFactory>();
            servises.AddSingleton<DownloadPaneViewModelFactory>();
            servises.AddSingleton<EditGameDataPaneViewModelFactory>();
            servises.AddSingleton<PowerPlayPaneViewModelFactory>();
            servises.AddSingleton<InstallModPaneViewModelFactory>();
            servises.AddSingleton<UserModelLoginPaneViewModelFactory>();

            provider = servises.BuildServiceProvider();
            PageContent.Content = new Home();
            // 注册消息
            WeakReferenceMessenger.Default.Register<MainWindowShowFlyoutMessage>(this, (re, message) => ShowFlyout(message.Title,message.Context,message.Type));
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
        foreach(var dis in Init.OnApplicationClosingReleaseSourcesList)
            dis.Dispose();
    }
    /// <summary>
    /// 在右下角显示提示信息
    /// </summary>
    /// <param ID="text">提示信息内容</param>
    public void ShowFlyout(string title,string text,NotificationType type = NotificationType.Information) =>
        Dispatcher.UIThread.Post(() =>
        NotificationManager.Show(new Notification(title, text, type, TimeSpan.FromSeconds(5))));
    
    // 统一事件方法
    private void ListBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox == null) return;

        var selectedItem = listBox.SelectedItem as ListBoxItem;
        if (selectedItem == null) return;

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
            case "Settings":
                PageContent.Content = settingsPage;
                break;
        }
        
    }
    // 切换侧边栏展开/折叠
    private void MangePaneOpenAndClose(bool IsOpen)
    {
        HomeText.IsVisible = IsOpen;
        VersionText.IsVisible = IsOpen;
        AccountText.IsVisible = IsOpen;
        DownloadText.IsVisible = IsOpen;
        SettingsText.IsVisible = IsOpen;
        //ServerText.IsVisible = IsOpen;
        ModsBrowserText.IsVisible = IsOpen;
        SidebarSplitView.IsPaneOpen = IsOpen;
    }
    // 鼠标进入事件
    private void Sb_in(object? sender, Avalonia.Input.PointerEventArgs e) => MangePaneOpenAndClose(true);
    // 鼠标离开事件
    private void Sb_out(object? sender, Avalonia.Input.PointerEventArgs e) => MangePaneOpenAndClose(false);
}