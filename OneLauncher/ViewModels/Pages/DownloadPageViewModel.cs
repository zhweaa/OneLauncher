using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Minecraft;
using OneLauncher.Views;
using OneLauncher.Views.Panes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Helper.Models;
using OneLauncher.ViewModel.Page;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Interfaces;

namespace OneLauncher.Views.ViewModels;
internal partial class DownloadPageViewModel : BaseViewModel,IPageHost,IListTemplate<VersionBasicInfo>
{
    private readonly DownloadPaneViewModelFactory _viewFactory;
    private readonly UnitedCore _unitedCore;
    // 这里要异步初始化的
    public async Task RefList()
    {
        try
        {
            var versionInfos =
                //await VersionsList.GetOrRefreshVersionListAsync();
                _unitedCore.MojangVersionList;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items = new ObservableCollection<VersionBasicInfo>(versionInfos); // 安全地创建集合
                IsLoaded = true;
            });
        }
        catch (OlanException e)
        {
            await OlanExceptionWorker.ForOlanException(e,() => _=RefList());
        }
    }
    public DownloadPageViewModel(DownloadPaneViewModelFactory viewFactory, UnitedCore unitedCore)
    {
        this._viewFactory = viewFactory;
        this._unitedCore = unitedCore;
        _=RefList();
    }
    [ObservableProperty] private bool isLoaded = false;
    [ObservableProperty] private ObservableCollection<VersionBasicInfo> items = new();
    [ObservableProperty] private VersionBasicInfo? selectedItem;
    partial void OnSelectedItemChanged(VersionBasicInfo value)
    {
        if(value != null)
            ToDownload(value);
    }
    [ObservableProperty] private UserControl? _paneContent;
    [ObservableProperty] public bool _IsPaneShow = false;
    [ObservableProperty] public bool _IsAllowDownloading;

    [RelayCommand]
    private void ToDownload(VersionBasicInfo vbi)
    {
        IsPaneShow = true;
        PaneContent = new DownloadPane()
        {DataContext = _viewFactory.Create(vbi,() => IsPaneShow = false)};
    }
}
