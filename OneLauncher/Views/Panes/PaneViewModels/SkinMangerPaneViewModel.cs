using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.Account.Microsoft;
using OneLauncher.Views.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;

internal partial class SkinMangerPaneViewModel : BaseViewModel
{
    private AccountPageViewModel accountPageViewModel;
    private UserModel SelUserModel;
    private readonly Action _onCloseCallback;
    public SkinMangerPaneViewModel() => _onCloseCallback = () => { };
    public SkinMangerPaneViewModel(AccountPageViewModel accountPageViewModel,UserModel SelUserModel, Action? onCloseCallback = null)
    {
        this.accountPageViewModel = accountPageViewModel;
        this.SelUserModel = SelUserModel;
        _onCloseCallback = onCloseCallback ?? (() => { });
    }
    private int _selectedIndex;
    public int SelectedIndex
    {
        set
        {
            _selectedIndex = value;
            if (value == 0)
            {
                IsUseFile = true;
                IsUseUrl = false;
            }
            else if (value == 1)
            {
                IsUseUrl = true;
                IsUseFile = false;
            }
        }
        get 
        { 
            return _selectedIndex;
        }
    }
    [ObservableProperty]
    public bool _IsSteveModel;
    [RelayCommand]
    public async Task ToChooseSkinFile()
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.mainwindow);
        if (topLevel?.StorageProvider is { } storageProvider && storageProvider.CanOpen)
        {
            // 配置文件选择器选项
            var options = new FilePickerOpenOptions
            {
                Title = "选择皮肤文件", // 对话框标题
                AllowMultiple = false, // 是否允许选择多个文件
                FileTypeFilter = new[]
                {
                    FilePickerFileTypes.ImagePng // 仅限png文件
                }
            };

            // 打开文件选择器
            var files = await storageProvider.OpenFilePickerAsync(options);
            var selectedFile = files.FirstOrDefault();

            if (files == null || !files.Any() || selectedFile == null)
                return;

            // 获取本地路径
            string filePath = selectedFile.Path.LocalPath;

            // 检查皮肤文件有效性
            if (!await MojangProfile.IsValidSkinFile(filePath))
            {
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("皮肤文件无效！", Avalonia.Controls.Notifications.NotificationType.Error));
                //MainWindow.mainwindow.ShowFlyout("皮肤文件无效！", true);
                return;
            }
            Debug.WriteLine("有效的皮肤文件");
            using (var task = new MojangProfile(SelUserModel))
            {
                // 上传
                await task.SetUseLocalFile(new MojangSkin()
                {
                    Skin = filePath,
                    IsSlimModel = (IsSteveModel) ? false : true
                });
                // 重新缓存本地皮肤文件
                await task.GetSkinHeadImage();
                // 刷新
                accountPageViewModel.RefList();

                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("已成功上传皮肤！", Avalonia.Controls.Notifications.NotificationType.Success));
                //MainWindow.mainwindow.ShowFlyout("已成功上传皮肤！");
            } 
        }
    }
    [ObservableProperty]
    public bool _IsUseUrl;
    [ObservableProperty]
    public bool _IsUseFile = true;
    [ObservableProperty]
    public string _Url;
    [RelayCommand]
    public async Task OpenInNameMC()
    {
        // 检查url有效性
        string Url = $"https://s.namemc.com/i/{this.Url}.png";
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Url));
            if (!response.IsSuccessStatusCode || Url == null)
            {
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("无效的ID", Avalonia.Controls.Notifications.NotificationType.Warning));
                return;
            }
        }
        using (var task = new MojangProfile(SelUserModel))
        {
            // 上传
            await task.SetUseUrl(new MojangSkin()
            {
                Skin = Url,
                IsSlimModel = (IsSteveModel) ? false : true
            });
            // 重新缓存本地皮肤文件
            await task.GetSkinHeadImage();
            // 刷新
            accountPageViewModel.RefList();

            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("已成功通过NameMC上传皮肤！", Avalonia.Controls.Notifications.NotificationType.Success));
        }
    }

    [RelayCommand]
    private void Back() => _onCloseCallback();
}
