using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.Server;
using OneLauncher.Views.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;

internal partial class EditServerPaneViewModel : BaseViewModel
{
    private readonly DBManager _dbManager;
    private readonly ServerEntry _editingServer;
    private readonly Action _onCloseCallback;
    private readonly Action? _onServerInfoUpdated;

    [ObservableProperty] private string serverName;
    [ObservableProperty] private string? serverDescription;
    [ObservableProperty] private string serverPort;
    [ObservableProperty] private string serverIP;
    [ObservableProperty] private Bitmap currentIcon;

    public string OriginalName => _editingServer.Name;
    public string Address => _editingServer.ReadableAddress;
    public string InstanceId => _editingServer.InstanceId;
    public string Id => _editingServer.Id.ToString();

    public EditServerPaneViewModel(
        DBManager dbManager,
        ServerEntry editingServer,
        Action onCloseCallback,
        Action? onServerInfoUpdated = null)
    {
        _dbManager = dbManager;
        _editingServer = editingServer;
        _onCloseCallback = onCloseCallback;
        _onServerInfoUpdated = onServerInfoUpdated;
        ServerName = editingServer.Name;
        ServerDescription = editingServer.Description;
        ServerIP = editingServer.ServerInfo.Ip;
        ServerPort = editingServer.ServerInfo.Port.ToString();
        CurrentIcon = ServerIconLoader.Load(editingServer);
    }

    [RelayCommand]
    private async Task ChangeIcon()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(MainWindow.mainwindow);
        if (topLevel?.StorageProvider is not { CanOpen: true } storageProvider)
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择服务器图标",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });

        var selectedFile = files.FirstOrDefault();
        if (selectedFile == null)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(_editingServer.IconFileUrl)!);
        string temporaryPath = $"{_editingServer.IconFileUrl}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using Stream sourceStream = await selectedFile.OpenReadAsync();
            using Bitmap selectedBitmap = new(sourceStream);
            await using (FileStream temporaryFile = File.Create(temporaryPath))
                selectedBitmap.Save(temporaryFile);
            File.Move(temporaryPath, _editingServer.IconFileUrl, true);

            CurrentIcon = ServerIconLoader.Load(_editingServer);
            await _dbManager.Save();
            ShowMessage($"已更改服务器“{_editingServer.Name}”的图标。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            ShowMessage($"更改服务器图标失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    [RelayCommand]
    private async Task GetServerInfo()
    {
        try
        {
            await _editingServer.GetAASServerInfo();
            ServerDescription = _editingServer.Description;
            CurrentIcon = ServerIconLoader.Load(_editingServer);
            await _dbManager.Save();
            _onServerInfoUpdated?.Invoke();
            ShowMessage("已更新服务器信息。", NotificationType.Success);
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex, () => _ = GetServerInfo());
        }
        catch (Exception ex)
        {
            await OlanExceptionWorker.ForUnknowException(ex, () => _ = GetServerInfo());
        }
    }

    [RelayCommand]
    private async Task AddToQuicklyPlay()
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            ShowMessage("无法获取启动器程序路径。", NotificationType.Error);
            return;
        }

        string shortcutName = GetSafeFileName(_editingServer.Name);
        string shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"快速加入 - {shortcutName}" +
#if WINDOWS
            ".bat"
#else
            ".sh"
#endif
        );

        string command =
#if WINDOWS
            $"@echo off{Environment.NewLine}\"{processPath}\" --joinServer {_editingServer.Id}{Environment.NewLine}";
#else
            $"#!/bin/sh{Environment.NewLine}\"{processPath}\" --joinServer {_editingServer.Id}{Environment.NewLine}";
#endif

        try
        {
            await File.WriteAllTextAsync(shortcutPath, command);
            ShowMessage("已创建服务器桌面快捷启动。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            ShowMessage($"创建桌面快捷启动失败：{ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
        {
            ShowMessage("服务器名称不能为空！", NotificationType.Warning);
            return;
        }

        _editingServer.Name = ServerName;
        _editingServer.Description = ServerDescription;
        if(ushort.TryParse(ServerPort, out ushort port))
        {
            if(port < 0 || port > 65535)
            {
                ShowMessage("端口格式不正确！", NotificationType.Warning);
                return;
            }
            _editingServer.ServerInfo = new ServerInfo(ServerIP, port);
        }
        else
        {
            ShowMessage("端口格式不正确！", NotificationType.Warning);
            return;
        }

        await _dbManager.Save();
        ShowMessage($"服务器收藏“{_editingServer.Name}”已保存。", NotificationType.Success);
        Cancel();
    }

    [RelayCommand]
    private async Task DeleteServer()
    {
        _dbManager.Data.ServerList.RemoveAll(server => server.Id == _editingServer.Id);
        if (_dbManager.Data.OlanSettings.DefaultServerID == _editingServer.Id)
            _dbManager.Data.OlanSettings.DefaultServerID = null;

        try
        {
            if (File.Exists(_editingServer.IconFileUrl))
                File.Delete(_editingServer.IconFileUrl);
        }
        catch (Exception ex)
        {
            ShowMessage($"删除服务器图标失败：{ex.Message}", NotificationType.Warning);
        }

        await _dbManager.Save();
        ShowMessage($"已删除服务器收藏“{_editingServer.Name}”。", NotificationType.Success);
        Cancel();
    }

    private static string GetSafeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string safeName = new(name
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(safeName) ? "服务器" : safeName;
    }

    private static void ShowMessage(string message, NotificationType type)
    {
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(message, type));
    }

    [RelayCommand]
    private void Cancel()
    {
        _onCloseCallback();
    }
}
