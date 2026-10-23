using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Server;
using OneLauncher.Views.Panes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OneLauncher.ViewModel.Page;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Interfaces;

namespace OneLauncher.Views.ViewModels;

internal static class ServerIconLoader
{
    public static Bitmap Load(ServerEntry serverEntry)
    {
        if (File.Exists(serverEntry.IconFileUrl))
        {
            try
            {
                return new Bitmap(serverEntry.IconFileUrl);
            }
            catch
            {
                // 损坏的本地图标不应阻止服务器列表显示。
            }
        }

        return new Bitmap(AssetLoader.Open(
            new Uri("avares://OneLauncher/Assets/Imgs/basic.png")));
    }
}

internal sealed class ServerItem : BaseViewModel
{
    private static readonly IBrush GoodPingBrush = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly IBrush WarningPingBrush = new SolidColorBrush(Color.Parse("#FF9F0A"));
    private static readonly IBrush PoorPingBrush = new SolidColorBrush(Color.Parse("#FF453A"));

    public ServerEntry data { get; }
    public Bitmap Icon { get; }
    public bool IsDefault { get; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(data.Description);
    public uint? Ping { get; }
    public uint? PlayersMax { get; }
    public bool HasPlayersMax => PlayersMax.HasValue;
    public string PlayersMaxText => PlayersMax is uint value ? $"最大人数: {value}" : string.Empty;
    public bool HasPing => Ping.HasValue;
    public string PingText => Ping switch
    {
        null => string.Empty,
        0 => "<1 ms",
        uint value => $"{value} ms"
    };
    public IBrush PingBrush => Ping switch
    {
        null => Brushes.Transparent,
        < 100 => GoodPingBrush,
        < 200 => WarningPingBrush,
        _ => PoorPingBrush
    };

    public ServerItem(ServerEntry serverEntry, Guid? defaultServerId, uint? ping)
    {
        data = serverEntry;
        Icon = ServerIconLoader.Load(serverEntry);
        IsDefault = serverEntry.Id == defaultServerId;
        Ping = ping;
        PlayersMax = serverEntry.PlayersMax;
    }
}

internal partial class ServerPageViewModel : BaseViewModel,IPageHost,IListTemplate<ServerItem>
{
    private readonly UnitedCore _unitedCore;
    private readonly DBManager _dbManager;
    public bool ShowIdentifiers => _dbManager.Data.OlanSettings.ShowIdentifiers;
    private GameDataManager _gameDataManager => _unitedCore.GameDataManager;
    private readonly AddServerPaneViewModelFactory _addServerPaneViewModelFactory;
    private readonly EditServerPaneViewModelFactory _editServerPaneViewModelFactory;
    private readonly EditGameDataPaneViewModelFactory _editGameDataPaneViewModelFactory;
    private readonly Dictionary<Guid, uint?> _pingByServerId = new();

    [ObservableProperty] private ObservableCollection<ServerItem> items = new();
    [ObservableProperty] private UserControl? paneContent;
    [ObservableProperty] private bool isPaneShow;

    public ServerPageViewModel(
        DBManager dbManager,
        UnitedCore unitedCore,
        AddServerPaneViewModelFactory addServerPaneViewModelFactory,
        EditServerPaneViewModelFactory editServerPaneViewModelFactory,
        EditGameDataPaneViewModelFactory editGameDataPaneViewModelFactory
        )
    {
        _unitedCore = unitedCore;
        _dbManager = dbManager;
        _addServerPaneViewModelFactory = addServerPaneViewModelFactory;
        _editServerPaneViewModelFactory = editServerPaneViewModelFactory;
        _editGameDataPaneViewModelFactory = editGameDataPaneViewModelFactory;
        _ = RefList();
        _dbManager.OnDataChanged += OnServerDataChanged;
        WeakReferenceMessenger.Default.Register<ServerPageViewModel, IdentifierVisibilityChangedMessage>(
            this, static (recipient, _) => Dispatcher.UIThread.Post(
                () => recipient.OnPropertyChanged(nameof(ShowIdentifiers))));
        _ = ProbePingsAsync();
    }
    #region
    public async Task RefList()
    {
        await Dispatcher.UIThread.InvokeAsync(RebuildList);
    }

    private void OnServerDataChanged() => _ = RefList();

    private void RebuildList()
    {
        Guid? defaultServerId = _dbManager.Data.OlanSettings.DefaultServerID;
        Items = new ObservableCollection<ServerItem>(_dbManager.Data.ServerList
            .Select(serverEntry => new ServerItem(
                serverEntry,
                defaultServerId,
                _pingByServerId.TryGetValue(serverEntry.Id, out uint? ping) ? ping : null))
            );
    }

    private async Task ProbePingsAsync()
    {
        ServerEntry[] entries = _dbManager.Data.ServerList.ToArray();
        uint?[] pings = await Task.WhenAll(entries.Select(ReadPingAsync));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int index = 0; index < entries.Length; index++)
                _pingByServerId[entries[index].Id] = pings[index];

            RebuildList();
        });
    }

    private static async Task<uint?> ReadPingAsync(ServerEntry serverEntry)
    {
        try
        {
            return await Task.Run(() => serverEntry.Ping).ConfigureAwait(false);
        }
        catch
        {
            // A malformed host or an unavailable ICMP provider should only hide the
            // optional indicator; it must not prevent the server card from rendering.
            return null;
        }
    }

    private void RefreshPing(Guid serverId)
    #endregion
    {
        _ = RefreshPingAsync(serverId);
    }

    private async Task RefreshPingAsync(Guid serverId)
    {
        ServerEntry? serverEntry = _dbManager.Data.ServerList
            .FirstOrDefault(server => server.Id == serverId);
        if (serverEntry == null)
            return;

        uint? ping = await ReadPingAsync(serverEntry);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _pingByServerId[serverId] = ping;
            RebuildList();
        });
    }

    ~ServerPageViewModel()
    {
        _dbManager.OnDataChanged -= OnServerDataChanged;
    }

    [RelayCommand]
    private void AddServer()
    {
        IsPaneShow = true;
        PaneContent = new AddServerPane
        {
            DataContext = _addServerPaneViewModelFactory.Create(
                () => IsPaneShow = false,
                serverId => RefreshPing(serverId))
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
                () => IsPaneShow = false,
                () => RefreshPing(serverEntry.Id),
                () => {
                    PaneContent = new EditGameDataPane()
                    {
                        DataContext = _editGameDataPaneViewModelFactory.Create(
                            _gameDataManager.GetInstanceFromId(serverEntry!.InstanceId!)!,
                            () => IsPaneShow = false)
                    };
                })
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
        GameData? gameData = _unitedCore.GameDataManager.GetInstanceFromId(serverEntry.InstanceId);
        if (gameData == null)
        {
            await OlanExceptionWorker.ForOlanException(
                new OlanException(
                    "启动失败",
                    $"没有找到服务器对应的游戏实例：{serverEntry.InstanceId}",
                    OlanExceptionAction.Error));
            return;
        }

        await Game.EasyGameLauncher(_unitedCore, gameData, serverEntry.ServerInfo, false);
    }
}
