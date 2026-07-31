using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Server;
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

    [ObservableProperty] private string serverName;
    [ObservableProperty] private string? serverDescription;

    public string OriginalName => _editingServer.Name;
    public string Address => $"{_editingServer.ServerInfo.Ip}:{_editingServer.ServerInfo.Port}";
    public string InstanceId => _editingServer.InstanceId;
    public string Id => _editingServer.Id.ToString();

    public EditServerPaneViewModel(
        DBManager dbManager,
        ServerEntry editingServer,
        Action onCloseCallback)
    {
        _dbManager = dbManager;
        _editingServer = editingServer;
        _onCloseCallback = onCloseCallback;
        ServerName = editingServer.Name;
        ServerDescription = editingServer.Description;
    }

    [RelayCommand]
    private void ChangeIcon()
    {
    }

    [RelayCommand]
    private void GetServerInfo()
    {
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

        _editingServer.Name = ServerName.Trim();
        _editingServer.Description = string.IsNullOrWhiteSpace(ServerDescription)
            ? null
            : ServerDescription.Trim();

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
