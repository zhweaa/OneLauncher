using OneLauncher.Interfaces;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.ViewModel.Page;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;
internal partial class NewGameDataPaneViewModel : BaseViewModel, IPaneHost
{
    private readonly VersionListManager _versionListManager;
    private readonly AccountManager _accountManager;
    private readonly GameDataManager _gameDataManager;
    public Action OnPaneClose { get; init; } = () => { };
    [ObservableProperty] private string gameDataName;
    [ObservableProperty] private List<UserVersion> availableBaseVersions;
    [ObservableProperty] private UserVersion selectedBaseVersion;
    [ObservableProperty] private List<ModEnum> availableModLoaders;
    [ObservableProperty] private ModEnum? selectedModLoader;
    [ObservableProperty] private List<UserModel> availableUsers;
    [ObservableProperty] private UserModel selectedUser;

    public NewGameDataPaneViewModel(
        VersionListManager versionListManager,
        AccountManager accountManager,
        GameDataManager gameDataManager,
        Action onCloseCallback,
        UserVersion? defaultBasicVerions = null)
    {
        _versionListManager = versionListManager;
        this._gameDataManager = gameDataManager;
        this._accountManager = accountManager;
        OnPaneClose = onCloseCallback ?? (() => { });
        // 加载所需数据
        AvailableBaseVersions = _versionListManager.GetVersions();
        AvailableUsers = _accountManager.GetAllUsers().ToList();
        SelectedUser = _accountManager.GetDefaultUser() ?? AvailableUsers.FirstOrDefault() ??
            new UserModel(Guid.NewGuid(),"GameDataDefault",Guid.NewGuid());

        // 默认情况下，模组加载器列表为空，等待用户选择基础版本
        AvailableModLoaders = new List<ModEnum> { ModEnum.none };
        SelectedModLoader = ModEnum.none;
        // 通过属性设置，才能初始化指定版本的加载器选项和实例名称。
        if (defaultBasicVerions != null)
            SelectedBaseVersion = AvailableBaseVersions.FirstOrDefault(
                version => version.VersionID == defaultBasicVerions.VersionID);
    }
    partial void OnSelectedBaseVersionChanged(UserVersion value)
    {
        if (value == null)
        {
            // 如果清空选择，则重置加载器列表
            AvailableModLoaders = new List<ModEnum> { ModEnum.none };
            SelectedModLoader = ModEnum.none;
            GameDataName = string.Empty;
            return;
        }

        var loaders = new List<ModEnum> { ModEnum.none };
        if (value.modType.IsFabric) loaders.Add(ModEnum.fabric);
        if (value.modType.IsQuilt) loaders.Add(ModEnum.quilt);
        if (value.modType.IsNeoForge) loaders.Add(ModEnum.neoforge);
        if (value.modType.IsForge) loaders.Add(ModEnum.forge);
        AvailableModLoaders = loaders;

        SelectedModLoader = value.modType.ToModEnum();
        GameDataName = $"{value.VersionID} - Instance";
    }

    [RelayCommand]
    private async Task Save()
    {
        // 数据验证
        if (SelectedBaseVersion == null)
        {
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage("请先选择一个基础游戏版本！",NotificationType.Warning));
            return;
        }
        if (string.IsNullOrWhiteSpace(GameDataName))
        {
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage("游戏数据名称不能为空！",NotificationType.Warning));
            return;
        }

        var loaderType = SelectedModLoader ?? ModEnum.none;

        var newGameData = new GameData(
            name: GameDataName,
            versionId: SelectedBaseVersion.VersionID,
            loader: loaderType, 
            userModel: SelectedUser.UserID
        );

        await _gameDataManager.AddGameDataAsync(newGameData);
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已成功创建游戏数据：{GameDataName}"));
        //MainWindow.mainwindow.ShowFlyout($"已成功创建游戏数据: {GameDataName}");

        //WeakReferenceMessenger.Default.Send(new GameDataPageDisplayListRefreshMessage());
        //MainWindow.mainwindow.gamedataPage.viewmodel.RefList();

        Cancel();
    }

    [RelayCommand]
    private void Cancel()
    {
        OnPaneClose();
        //MainWindow.mainwindow.gamedataPage.viewmodel.IsPaneShow = false;
    }
}
