using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Server;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels.Factories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneLauncher.Views.ViewModels;

internal sealed class ServerItem : BaseViewModel
{
    public ServerEntry data { get; }
    public bool IsDefault { get; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(data.Description);

    public ServerItem(ServerEntry serverEntry, Guid? defaultServerId)
    {
        data = serverEntry;
        IsDefault = serverEntry.Id == defaultServerId;
    }
}

internal partial class ServerPageViewModel : BaseViewModel
{
    private readonly DBManager _dbManager;
    private readonly AddServerPaneViewModelFactory _addServerPaneViewModelFactory;
    private readonly EditServerPaneViewModelFactory _editServerPaneViewModelFactory;

    public List<ServerItem> ServerList { get; private set; } = new();
    [ObservableProperty] private UserControl? paneContent;
    [ObservableProperty] private bool isPaneShow;

    public ServerPageViewModel(
        DBManager dbManager,
        AddServerPaneViewModelFactory addServerPaneViewModelFactory,
        EditServerPaneViewModelFactory editServerPaneViewModelFactory)
    {
        _dbManager = dbManager;
        _addServerPaneViewModelFactory = addServerPaneViewModelFactory;
        _editServerPaneViewModelFactory = editServerPaneViewModelFactory;
        RefList();
        _dbManager.OnDataChanged += RefList;
    }

    private void RefList()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Guid? defaultServerId = _dbManager.Data.OlanSettings.DefaultServerID;
            ServerList = _dbManager.Data.ServerList
                .Select(serverEntry => new ServerItem(serverEntry, defaultServerId))
                .ToList();
            OnPropertyChanged(nameof(ServerList));
        });
    }

    ~ServerPageViewModel()
    {
        _dbManager.OnDataChanged -= RefList;
    }

    [RelayCommand]
    private void AddServer()
    {
        IsPaneShow = true;
        PaneContent = new AddServerPane
        {
            DataContext = _addServerPaneViewModelFactory.Create(() => IsPaneShow = false)
        };
    }

    [RelayCommand]
    private void EditServer(ServerEntry serverEntry)
    {
        IsPaneShow = true;
        PaneContent = new EditServerPane
        {
            DataContext = _editServerPaneViewModelFactory.Create(
                serverEntry,
                () => IsPaneShow = false)
        };
    }

    [RelayCommand]
    private async Task SetAsDefaultServer(ServerEntry serverEntry)
    {
        _dbManager.Data.OlanSettings.DefaultServerID = serverEntry.Id;
        await _dbManager.Save();
    }

    [RelayCommand]
    private async Task LaunchServer(ServerEntry serverEntry)
    {
        GameData? gameData = Init.GameDataManger.GetInstanceFromId(serverEntry.InstanceId);
        if (gameData == null)
        {
            await OlanExceptionWorker.ForOlanException(
                new OlanException(
                    "启动失败",
                    $"没有找到服务器对应的游戏实例：{serverEntry.InstanceId}",
                    OlanExceptionAction.Error));
            return;
        }

        await Game.EasyGameLauncher(gameData, serverEntry.ServerInfo, false);
    }
}
