using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Server;
using OneLauncher.Views.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;

internal partial class AddServerPaneViewModel : BaseViewModel
{
    private readonly DBManager _dbManager;
    private readonly Action _onCloseCallback;

    [ObservableProperty] private List<GameData> availableInstances;
    [ObservableProperty] private GameData? selectedInstance;
    [ObservableProperty] private string serverAddress = string.Empty;
    [ObservableProperty] private string serverPort = "25565";
    [ObservableProperty] private string serverName = string.Empty;
    [ObservableProperty] private string? serverDescription;

    public AddServerPaneViewModel(
        DBManager dbManager,
        GameDataManager gameDataManager,
        Action onCloseCallback)
    {
        _dbManager = dbManager;
        _onCloseCallback = onCloseCallback;
        AvailableInstances = gameDataManager.AllGameData;

        string? defaultInstanceId = _dbManager.Data.OlanSettings.DefaultInstanceID;
        SelectedInstance = defaultInstanceId == null
            ? AvailableInstances.FirstOrDefault()
            : gameDataManager.GetInstanceFromId(defaultInstanceId) ?? AvailableInstances.FirstOrDefault();
    }

    partial void OnServerAddressChanged(string? oldValue, string newValue)
    {
        if (string.IsNullOrWhiteSpace(ServerName) || ServerName == oldValue?.Trim())
            ServerName = newValue.Trim();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (SelectedInstance == null)
        {
            ShowWarning("请先选择一个游戏实例！");
            return;
        }

        if (string.IsNullOrWhiteSpace(ServerAddress))
        {
            ShowWarning("服务器地址不能为空！");
            return;
        }

        if (!ushort.TryParse(ServerPort, out ushort port) || port == 0)
        {
            ShowWarning("服务器端口必须是 1 到 65535 之间的数字！");
            return;
        }

        if (string.IsNullOrWhiteSpace(ServerName))
        {
            ShowWarning("服务器名称不能为空！");
            return;
        }

        string? description = string.IsNullOrWhiteSpace(ServerDescription)
            ? null
            : ServerDescription.Trim();

        var serverEntry = new ServerEntry(
            Guid.NewGuid(),
            SelectedInstance.InstanceId,
            new ServerInfo
            {
                Ip = ServerAddress.Trim(),
                Port = port.ToString()
            },
            ServerName.Trim(),
            description);

        _dbManager.Data.ServerList.Add(serverEntry);
        await _dbManager.Save();

        WeakReferenceMessenger.Default.Send(
            new MainWindowShowFlyoutMessage(
                $"已添加服务器收藏：{serverEntry.Name}",
                NotificationType.Success));
        Cancel();
    }

    private static void ShowWarning(string message)
    {
        WeakReferenceMessenger.Default.Send(
            new MainWindowShowFlyoutMessage(message, NotificationType.Warning));
    }

    [RelayCommand]
    private void Cancel()
    {
        _onCloseCallback();
    }
}
