using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using OneLauncher.Core.Mod.ModPack;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;
using OneLauncher.Views.Panes.PaneViewModels.Factories;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Views.ViewModels;
//internal class GameDataPageDisplayListRefreshMessage { }
internal sealed class GameDataTagCreatedMessage { }

internal partial class GameDataItem : BaseViewModel
{
    public GameData data { get; set; }
    public Bitmap Icon { get; set; }
    public bool IsDefault { get; set; }
    public bool IsUseDebugModLaunch {  get; set; }
    public bool IsMod => data.ModLoader != ModEnum.none;
    public string? QuicklyServerInfoIP { get; set; }
    public string? QuicklyServerInfoPort { get; set; }
    [RelayCommand]
    public void Launch(GameData gameData)
    {
        ServerInfo? quicklyPlayServerInfo = null;
        if(QuicklyServerInfoIP != null)
        {
            ushort quicklyPlayPort = ushort.TryParse(QuicklyServerInfoPort, out ushort parsedPort)
                ? parsedPort
                : (ushort)25565;
            quicklyPlayServerInfo = new ServerInfo
            {
                Ip = QuicklyServerInfoIP,
                Port = quicklyPlayPort
            };
        }
        _=Game.EasyGameLauncher(
            gameData,
            useDebugMode: IsUseDebugModLaunch,
            serverInfo: quicklyPlayServerInfo
            );
    }
    public GameDataItem(GameData gameData,GameDataManager gameDataManager)
    {
        data = gameData;

        // 检查自己是否是其对应版本的默认实例
        var defaultInstance = gameDataManager.GetDefaultInstance(gameData.VersionId);
        IsDefault = (defaultInstance != null && defaultInstance.InstanceId == gameData.InstanceId);
        Icon = GetGameDataIcon(gameData);
    }
    public static Bitmap GetGameDataIcon(GameData? data)
    {
        if (data == null)
            return new Bitmap(AssetLoader.Open(new Uri("avares://OneLauncher/Assets/Imgs/basic.png")));
        var customIconPath = Path.Combine(data.InstancePath, ".olc", "customicon");
        if (File.Exists(customIconPath))
        {
            try { return new Bitmap(customIconPath); }
            catch (Exception) { /* 忽略错误，使用默认图标 */ }
        }
        string iconUri = data.ModLoader switch
        {
            ModEnum.fabric => "avares://OneLauncher/Assets/Imgs/fabric.png",
            ModEnum.quilt => "avares://OneLauncher/Assets/Imgs/quilt.png",
            ModEnum.neoforge => "avares://OneLauncher/Assets/Imgs/neoforge.png",
            ModEnum.forge => "avares://OneLauncher/Assets/Imgs/forge.jpg",
            _ => "avares://OneLauncher/Assets/Imgs/basic.png", // 草方块
        };
        return new Bitmap(AssetLoader.Open(new Uri(iconUri)));
    }
}
internal partial class GameDataTagItem : BaseViewModel
{
    private readonly GameDataManager? _gameDataManager;
    public GameDataTag? Data { get; }
    public bool IsCreateAction { get; }
    public bool CanDelete => !IsCreateAction;
    public string DisplayName => IsCreateAction ? "新建标签..." : Data?.Name ?? string.Empty;

    private GameDataTagItem()
    {
        IsCreateAction = true;
    }

    public GameDataTagItem(GameDataTag tag, GameDataManager? gameDataManager)
    {
        Data = tag;
        _gameDataManager = gameDataManager;
    }

    public static GameDataTagItem CreateAction() => new();

    [RelayCommand]
    private async Task DeleteTag()
    {
        if (Data is null || _gameDataManager is null) return;
        await _gameDataManager.RemoveTagAsync(Data.ID);
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"标签 '{Data.Name}' 已删除", NotificationType.Success));
    }
}
internal partial class GameDataPageViewModel : BaseViewModel
{
    private readonly GameDataManager _gameDataManager;
    private readonly EditGameDataPaneViewModelFactory _editVMFactory;
    private readonly NewGameDataPaneViewModelFactory _newGameDataPaneViewModelFactory;
    [ObservableProperty] public List<GameDataItem> gameDataList = new();
    [ObservableProperty] public string type;
    [ObservableProperty] public UserControl paneContent;
    [ObservableProperty] public bool isPaneShow;
    // 刷新列表
    public void RefList()
    {
        Dispatcher.UIThread.Post(() => 
        {
            GameDataList = _gameDataManager.AllGameData.Select(x => new GameDataItem(x, _gameDataManager)).ToList();
            AvailableTags = BuildAvailableTags();
        });
    }

    private ObservableCollection<GameDataTagItem> BuildAvailableTags()
    {
        var items = new ObservableCollection<GameDataTagItem>
        {
            GameDataTagItem.CreateAction()
        };
        foreach (var tag in _gameDataManager.Data.Tags.Values)
            items.Add(new GameDataTagItem(tag, _gameDataManager));
        return items;
    }
    
    public GameDataPageViewModel(
        GameDataManager gameDataManager,
        EditGameDataPaneViewModelFactory editGameDataPaneViewModelFactory,
        NewGameDataPaneViewModelFactory newGameDataPaneViewModelFactory
        )
    {
        this._newGameDataPaneViewModelFactory = newGameDataPaneViewModelFactory;
        this._editVMFactory = editGameDataPaneViewModelFactory;
        this._gameDataManager = gameDataManager;
        AvailableTags = BuildAvailableTags();
#if DEBUG
        // 造密码的Avalonia设计器天天报错
        // 设计时数据
        if (Design.IsDesignMode)
        {
            // 创建一个临时的、仅用于设计的假用户模型
            var designTimeUser = Guid.NewGuid();
            var gameData1 = new GameData(
                name: "纯净生存 (设计时)",
                versionId: "1.21",
                loader: ModEnum.none,
                userModel: designTimeUser
            );

            var gameData2 = new GameData(
                name: "Fabric 模组包 (设计时)",
                versionId: "1.20.4",
                loader: ModEnum.fabric,
                userModel: designTimeUser
            );

            var gameTag1 = new GameDataTagItem(new GameDataTag
            {
                Name = "生存",
                ID = Guid.NewGuid()
            }, null);

            // 将创建好的 GameData 包装成 GameDataItem 并添加到列表
            GameDataList = new List<GameDataItem>()
            {
                new GameDataItem(gameData1,_gameDataManager),
                new GameDataItem(gameData2,_gameDataManager)
            };
            AvailableTags = new ObservableCollection<GameDataTagItem>()
            {
                GameDataTagItem.CreateAction(),
                gameTag1
            };
        }
        else
#endif
        {
            RefList();
            _gameDataManager.OnDataChanged += RefList;
        }
    }
    ~GameDataPageViewModel()
    {
        _gameDataManager.OnDataChanged -= RefList;
    }
    [RelayCommand]
    public void ModsManager(GameData data)
    {
        IsPaneShow = true;
        PaneContent = new GameDataModsManagerPane()
        { DataContext = new GameDataModsManagerPaneViewModel(data,() => IsPaneShow = false) };
    }
    [RelayCommand]
    public void NewGameData()
    {
        IsPaneShow = true;
        PaneContent = new NewGameDataPane()
        { DataContext =  _newGameDataPaneViewModelFactory.Create(() => IsPaneShow = false)};
    }
    [RelayCommand]
    public void ShowEditPane(GameData data)
    {
        IsPaneShow = true;
        PaneContent = new EditGameDataPane()
        { DataContext = _editVMFactory.Create(data,() => IsPaneShow = false) };
    }
    [RelayCommand]
    private async Task SetAsDefaultInstance(GameDataItem targetData)
    {
        //GameDataList.Select(x => x.IsDefault = false); // 先将所有实例的IsDefault设为false
        //targetData.IsDefault = true; // 将目标实例的IsDefault设为true
        await _gameDataManager.SetDefaultInstanceAsync(targetData.data.InstanceId);
        RefList();
        WeakReferenceMessenger.Default.Send(
            new MainWindowShowFlyoutMessage($"已将 '{targetData.data.Name}' 设为版本 {targetData.data.VersionId} 的默认实例。"));
    }
    #region 顶层按钮事件
    [ObservableProperty]
    private ObservableCollection<GameDataTagItem> availableTags;
    [ObservableProperty]
    private GameDataTagItem? selectedTag;
    [ObservableProperty]
    private string? newTagName = string.Empty;
    [RelayCommand]
    private async Task CreateNewTag()
    {
        if(string.IsNullOrWhiteSpace(NewTagName)) return;
        var tagName = NewTagName.Trim();
        var newTagId = Guid.NewGuid();
        await _gameDataManager.CreateTag(new GameDataTag
        {
            Name = tagName,
            ID = newTagId
        },newTagId);
        NewTagName = string.Empty;
        SelectedTag = null;
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"标签 '{tagName}' 已创建", NotificationType.Success));
        WeakReferenceMessenger.Default.Send(new GameDataTagCreatedMessage());
    }
    partial void OnSelectedTagChanged(GameDataTagItem? value)
    {
        if (value?.IsCreateAction == true) return;
    }
    [RelayCommand]
    private void ResetFilter()
    {
        SelectedTag = null; // 里面有空值检查所以这里手动刷新
        RefList();
    }
    [RelayCommand]
    public void Sorting(SortingType type)
    {
        List<GameDataItem> orderedList = type switch
        {
            SortingType.AnTime_OldFront => GameDataList.OrderBy(x => x.data.CreationTime).ToList(),
            SortingType.AnTime_NewFront => GameDataList.OrderByDescending(x => x.data.CreationTime).ToList(),
            SortingType.AnVersion_OldFront => GameDataList.OrderBy(x => new Version(x.data.VersionId)).ToList(),
            SortingType.AnVersion_NewFront => GameDataList.OrderByDescending(x => new Version(x.data.VersionId)).ToList(),
            _ => GameDataList // 默认不排序
        };
        _gameDataManager.Data.Instances = GameDataList.ToDictionary(x => x.data.InstanceId,x => x.data);
        _=_gameDataManager.Save();
    }
    #endregion
}
