using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.ConnectToolPower;
using OneLauncher.Views.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;
public enum PowerPlayMode { Host, Join }
public partial class PowerPlayPaneViewModel : BaseViewModel
{
    private readonly GameDataManager _gameDataManager;
    ~PowerPlayPaneViewModel()
    {
        mainPower.CoreLog -= OnCoreLogReceived;
        _gameDataManager.OnDataChanged -= OnListRefreshing;
        Stop();
    }
    public PowerPlayPaneViewModel(IConnectService connectService,MCTPower mainPower,GameDataManager gameDataManager)
    {
        this._gameDataManager = gameDataManager;
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsHostModeChecked) && IsHostModeChecked)
            {
                SetProperty(ref isJoinModeChecked, false, nameof(IsJoinModeChecked));
            }
            else if (e.PropertyName == nameof(IsJoinModeChecked) && IsJoinModeChecked)
            {
                SetProperty(ref isHostModeChecked, false, nameof(IsHostModeChecked));
            }
        };
        Init.OnApplicationClosingReleaseSourcesList.Add(mainPower);
        AvailableGameData = _gameDataManager.AllGameData;
        mainPower.CoreLog += OnCoreLogReceived;
        this.mainPower = mainPower;
        this.connectService = connectService;
        _gameDataManager.OnDataChanged += OnListRefreshing;
    }
    private readonly MCTPower mainPower;
    private readonly IConnectService connectService;
    #region 模型定义
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private bool isConnected = false;

    [ObservableProperty]
    private bool isHostModeChecked = true;

    [ObservableProperty]
    private bool isJoinModeChecked = false;

    [ObservableProperty] private string hostRoomCode = string.Empty;
    [ObservableProperty] private string joinRoomCode = string.Empty;
    [ObservableProperty] private string joinPort = string.Empty;
    [ObservableProperty] private string localServerAddress = string.Empty;
    [ObservableProperty] private string logOutput = string.Empty;

    // 获取当前可用的所有可以游戏实例
    [ObservableProperty]
    public List<GameData> availableGameData;

    // 那个要作为主机的实例
    [ObservableProperty]
    public GameData? selectedHostGameData;

    private readonly StringBuilder logBuilder = new();

    public bool CanStart => !isConnected;
    public bool CanStop => isConnected;
    #endregion
    [RelayCommand]
    private async Task Host()
    {
        try
        {
            if (SelectedHostGameData == null)
                throw new OlanException("未能创建房间", "请先在下拉框中选择一个要进行联机的游戏版本。", OlanExceptionAction.Warning);

            string p2pNodeName = "OLANNODE" + RandomNumberGenerator.GetInt32(100000, 1000000000);
            string combinedInfo = $"{p2pNodeName}:{SelectedHostGameData.VersionId}";
            string finalRoomCode = TextHelper.Base64Encode(combinedInfo);

            HostRoomCode = finalRoomCode;
            IsConnected = true;
            connectService.StartAsHost(p2pNodeName, null);
            version.EasyGameLauncher(SelectedHostGameData); // 直接帮他把游戏启动了
        }
        catch (OlanException olanEx)
        {
            await OlanExceptionWorker.ForOlanException(olanEx);
        }
        catch (Exception ex)
        {
            await OlanExceptionWorker.ForUnknowException(ex);
        }
    }

    [RelayCommand]
    private async Task JoinAndLaunch()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(JoinRoomCode))
                throw new OlanException("输入无效", "必须输入房间码。", OlanExceptionAction.Warning);

            if (string.IsNullOrWhiteSpace(JoinPort) || !int.TryParse(JoinPort, out int destPort) || destPort is < 1 or > 65535)
                throw new OlanException("输入无效", "请输入一个有效的目标端口号 (1-65535)。", OlanExceptionAction.Warning);

            string p2pNodeName;
            string versionId = string.Empty;
            bool isMCTMode = false;

            try
            {
                string decodedInfo = TextHelper.Base64Decode(JoinRoomCode);
                string[] parts = decodedInfo.Split(':', 2);
                if (parts.Length < 2 || !parts[0].StartsWith("OLANNODE")) throw new FormatException("不是有效的OneLauncher房间码格式。");
                p2pNodeName = parts[0];
                versionId = parts[1];
            }
            // 尝试兼容MCT
            catch (FormatException)
            {
                if (JoinRoomCode.StartsWith("M") && JoinRoomCode.EndsWith("C"))
                {
                    p2pNodeName = JoinRoomCode;
                    isMCTMode = true;
                }
                else
                    throw new OlanException("房间码无效", "无法解析此房间码，请确认是否从OneLauncher主机处复制。", OlanExceptionAction.Error);
                
            }

            int localPort = Tools.GetFreeTcpPort();
            LocalServerAddress = $"127.0.0.1:{localPort}";
            IsConnected = true;

            if (isMCTMode)
            {
                // **纯MCT兼容模式：只启动P2P，不启动游戏**
                connectService.Join(null, p2pNodeName, localPort, destPort, null, null, null);
                LogMessage($"已进入MCT兼容模式，仅启动P2P连接服务，目标节点: {p2pNodeName}");
            }
            else
            {
                var allInstancesForVersion = _gameDataManager.AllGameData;

                if (!allInstancesForVersion.Any())
                {
                    throw new OlanException("加入失败", $"你还没有安装版本 {versionId} 的任何游戏实例。", OlanExceptionAction.Error);
                }

                // 查找或设置要启动的实例
                GameData instanceToLaunch; 
                var defaultInstance = _gameDataManager.GetDefaultInstance(versionId);

                if (defaultInstance != null)
                {
                    instanceToLaunch = defaultInstance;
                }
                else
                {
                    // 如果没有默认项，自动设置第一个并通知用户
                    instanceToLaunch = allInstancesForVersion.First();
                    await _gameDataManager.SetDefaultInstanceAsync(instanceToLaunch.InstanceId);
                    WeakReferenceMessenger.Default.Send(
                        new MainWindowShowFlyoutMessage($"已自动将'{instanceToLaunch.Name}'设为版本{versionId}的默认实例"));
                    //MainWindow.mainwindow.ShowFlyout($"已自动将'{instanceToLaunch.Name}'设为版本{versionId}的默认实例");
                }

                // 启动P2P
                mainPower.ConnectionEstablished += () =>
                {
                    version.EasyGameLauncher(instanceToLaunch, serverInfo: new ServerInfo
                    {
                        Ip = "127.0.0.1",
                        Port = localPort.ToString()
                    });
                };

                connectService.Join(null, p2pNodeName, localPort, destPort, null, null, null);
                LogMessage($"P2P启动，准备连接到 {p2pNodeName} 并启动游戏...");
            }
        }
        catch (OlanException olanEx)
        {
            await OlanExceptionWorker.ForOlanException(olanEx);
            Stop();
        }
        catch (Exception ex)
        {
            await OlanExceptionWorker.ForUnknowException(ex);
            Stop();
        }
    }
    [RelayCommand]
    private Task CopyCode() =>
        TopLevel.GetTopLevel(MainWindow.mainwindow)!.Clipboard!.SetTextAsync(IsHostModeChecked ? HostRoomCode : JoinRoomCode);
    
    [RelayCommand]
    private void Stop()
    {
        mainPower.Dispose();
        IsConnected = false;
        HostRoomCode = string.Empty;
        LocalServerAddress = string.Empty;
        LogMessage("连接已断开。");
    }

    private void OnCoreLogReceived(string logMessage) => Dispatcher.UIThread.Post(() => LogMessage(logMessage));
    private void OnListRefreshing() => Dispatcher.UIThread.Post(() => AvailableGameData = _gameDataManager.AllGameData);
    private void LogMessage(string message) { logBuilder.AppendLine(message); LogOutput = logBuilder.ToString(); }
}
