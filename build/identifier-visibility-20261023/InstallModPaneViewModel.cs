using OneLauncher.Interfaces;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.ViewModel.Page;
using OneLauncher.Views.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;

internal partial class InstallModPaneViewModel : BaseViewModel, IPaneHost
{
    private readonly GameDataManager _gameDataManager;
    private readonly UnitedCore _unitedCore;
#if DEBUG
    // 给设计器预览的
    public InstallModPaneViewModel()
    {
        if (Design.IsDesignMode)
            ModName = "暮色森林";
    }
#endif
    private readonly ModItem modItem;
    public Action OnPaneClose { get; init; } = () => { };
    public InstallModPaneViewModel(ModItem item, GameDataManager gameDataManager, UnitedCore unitedCore, Action onCloseCallback)
    {
        this._gameDataManager = gameDataManager;
        _unitedCore = unitedCore;
        modItem = item;
        ModName = item.Title;
        SupportVersions = item.SupportVersions;
        AvailableGameData = _gameDataManager.AllGameData;
        this.SupportModType = item.SupportModType;
        OnPaneClose = onCloseCallback ?? (() => { });
    }
    private ModType SupportModType;
    [ObservableProperty]
    public bool _IsShowMoreInfo = true;
    [ObservableProperty]
    public string _ModName;
    [ObservableProperty]
    public List<string> _SupportVersions;

    // --- 修改点 2：添加用于绑定的属性 ---
    [ObservableProperty]
    private List<GameData> _AvailableGameData;

    [ObservableProperty]
    private GameData? _SelectedGameData; 
    [RelayCommand]
    public async Task ToInstall()
    {
        // 验证是否选择了实例
        if (SelectedGameData == null)
        {
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage("请先选择一个游戏实例！"));
            //MainWindow.mainwindow.ShowFlyout("请先选择一个游戏实例！", true);
            return;
        }
        // 检查加载器是否匹配
        if (SupportModType != SelectedGameData.ModLoader)
        {
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage($"此 Mod 不支持 {SelectedGameData.ModLoader} 加载器。"));
            //MainWindow.mainwindow.ShowFlyout($"此 Mod 不支持 {SelectedGameData.ModLoader} 加载器。", true);
            return;
        }
        // 检查游戏版本是否受支持
        if (!SupportVersions.Contains(SelectedGameData.VersionId))
        {
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage($"此 Mod 不支持游戏版本 {SelectedGameData.VersionId}。"));
            //MainWindow.mainwindow.ShowFlyout($"此 Mod 不支持游戏版本 {SelectedGameData.VersionId}。", true);
            return;
        }
        // 4. 所有检查通过，开始下载
        IsShowMoreInfo = false;
        try
        {
            // 目标路径现在直接从 SelectedGameData 获取
            string modsPath = Path.Combine(SelectedGameData.InstancePath, "mods");

            await _unitedCore.ResourceInstallerService.InstallMod(
                new Progress<(long all, long done, double percentage, string c)>(p => Dispatcher.UIThread.Post(() =>
                {
                    if (p.all > 0) // 避免除零错误
                    {
                        CurrentProgress = (double)p.done / p.all * 100;
                        Fs = $"{p.done}/{p.all}";
                    }
                    Dp = p.c; // 更新文件名或状态
                })),
                modItem.ID,
                SelectedGameData,
                SelectedGameData.ModLoader,
                includeDependencies: IsICS,
                // 先凑合用，未来再重写
                verifySha1: Init.ConfigManger.Data.OlanSettings.IsSha1Enabled
            );
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage($"{ModName} 安装成功！"));
            //MainWindow.mainwindow.ShowFlyout($"{ModName} 安装成功！");
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex);
            IsShowMoreInfo = true; // 失败后恢复界面
        }
        catch (Exception ex)
        {
            await OlanExceptionWorker.ForUnknowException(ex);
            IsShowMoreInfo = true; // 失败后恢复界面
        }
    }

    [ObservableProperty]
    public bool _IsICS = true; // 默认勾选下载依赖

    [ObservableProperty]
    public string _Dp = "等待安装";

    [ObservableProperty]
    public string _Fs = "0/0";

    [ObservableProperty]
    public double _CurrentProgress = 0;

    [RelayCommand]
    public void ClosePane()
    {
        OnPaneClose();
        //MainWindow.mainwindow.modsBrowserPage.viewmodel.IsPaneShow = false;
    }
}


