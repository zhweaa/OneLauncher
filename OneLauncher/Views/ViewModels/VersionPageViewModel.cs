using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Compatible.ImportPCL2Version;
using OneLauncher.Core.Downloader;
using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.ImportPCL2Version;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Launcher;
using OneLauncher.Core.Minecraft.Server;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;
using OneLauncher.Views.Panes.PaneViewModels.Factories;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace OneLauncher.Views.ViewModels;
internal partial class VersionItem : BaseViewModel
{
    /// <param Name="a">UserVersion实例</param>
    /// <param Name="IndexInInit">UserVsersion实例在整个Init.ConfigManager.config.VersionList中的索引值</param>
    public VersionItem(UserVersion a)
    {
        versionExp = a;
    }
    public UserVersion versionExp { get; set; }
    [RelayCommand]
    public async Task LaunchGame()
    {
        _=new GameLauncher().Play(versionExp, useRootMode: true);
    }
    [RelayCommand]
    public void ReadMoreInformations()
    {
        Tools.OpenWebsite($"https://zh.minecraft.wiki/w/Java版{versionExp.VersionID}");
    }
    [RelayCommand]
    public void OpenServerFolder()
    {
        string path = Path.Combine(Init.GameRootPath,"versions",versionExp.VersionID,"servers");
        if (!Directory.Exists(path))
            OlanExceptionWorker.ForOlanException(
                new OlanException("无法打开服务端文件夹","服务端尚未初始化",OlanExceptionAction.Error));
        else
            Tools.OpenFolder(path);
    }
}
internal partial class VersionPageViewModel : BaseViewModel
{
    private readonly DBManager _dBManager;
    private readonly NewGameDataPaneViewModelFactory _newGameDataPaneViewModelFactory;
    private void RefList()
    {
        VersionList = _dBManager.Data.VersionList.Select(x => new VersionItem(x)).ToList();
    }
    public VersionPageViewModel(DBManager dBManager, NewGameDataPaneViewModelFactory newGameDataPaneViewModelFactory)
    {
        this._dBManager = dBManager;
        _newGameDataPaneViewModelFactory = newGameDataPaneViewModelFactory;
#if DEBUG
        // 设计时数据
        if (Design.IsDesignMode)
        {
            VersionList = new List<VersionItem>()
            {
                new VersionItem(new UserVersion() 
                {
                    VersionID="1.21.5",
                    AddTime=DateTime.Now
                })
            };
        }
        else
#endif
        {
            try
            {
                RefList();
                _dBManager.OnDataChanged += () => Dispatcher.UIThread.Post(() => RefList());
            }
            catch (NullReferenceException ex)
            {
                throw new OlanException(
                    "内部异常",
                    "配置文件特定部分版本部分为空，这可能是新版和旧版配置文件不兼容导致的",
                    OlanExceptionAction.FatalError,
                    ex,
                   () => {
                       File.Delete(Path.Combine(Init.BasePath, "config.json"));
                       Init.Initialize();
                   });
            }
        } 
    }
    [ObservableProperty]
    public List<VersionItem> _versionList;
    [ObservableProperty]
    public UserControl _refDownPane;
    [ObservableProperty]
    public bool _isPaneShow;
    [RelayCommand]
    public void Sorting(SortingType type)
    {
        List<VersionItem> orderedList = type switch
        {
            SortingType.AnTime_OldFront => VersionList.OrderBy(x => x.versionExp.AddTime).ToList(),
            SortingType.AnTime_NewFront => VersionList.OrderByDescending(x => x.versionExp.AddTime).ToList(),
            SortingType.AnVersion_OldFront => VersionList.OrderBy(x => new Version(x.versionExp.VersionID)).ToList(),
            SortingType.AnVersion_NewFront => VersionList.OrderByDescending(x => new Version(x.versionExp.VersionID)).ToList(),
            _ => VersionList // 默认不排序
        };

        VersionList = orderedList; 
        _dBManager.Data.VersionList = VersionList.Select(x => x.versionExp).ToList();
        _=_dBManager.Save();
    }
    [RelayCommand]
    public async Task OpenServer(UserVersion versionExp)
    {
        try
        {
            // 去尝试读取，判断这个服务端版本是否启用了版本隔离
            bool IsVI = true;
            if (Directory.Exists(Path.Combine(Init.GameRootPath, "versoins", versionExp.VersionID, "servers")))
                IsVI = true;
            else if (Directory.Exists(Path.Combine(Init.GameRootPath, "servers")))
                IsVI = false;
            string versionPath = Path.Combine(Init.GameRootPath, "versions", versionExp.VersionID);
            // 判断服务端是否已经完成初始化
            if (!File.Exists(Path.Combine(versionPath, "server.jar")))
            {
                IsPaneShow = true;
                RefDownPane = new InitServerPane()
                { DataContext = new InitServerPaneViewModel(versionExp.VersionID,() => IsPaneShow = false)};
            }
            else
                using (var fs = File.OpenRead(
                                Path.Combine(versionPath, $"version.json")))
                    MinecraftServerManger.Run(versionPath,
                        // 读取源文件获取Java版本
                        (await JsonNode.ParseAsync(
                                    fs))
                                    ?["javaVersion"]
                                    ?["majorVersion"]
                                    ?.GetValue<int>()
                                    ?? Tools.ForNullJavaVersion(versionExp.VersionID)
                                    , IsVI);
            
            
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex);
        }
    }
    [RelayCommand]
    public void CreateGameData(UserVersion versionExp)
    {
        IsPaneShow = true;
        RefDownPane = new NewGameDataPane()
        { DataContext = _newGameDataPaneViewModelFactory.Create(versionExp, () => IsPaneShow = false) };
    }
}

