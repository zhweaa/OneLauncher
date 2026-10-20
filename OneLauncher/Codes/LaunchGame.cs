using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Launcher;
using OneLauncher.Core.Minecraft;
using OneLauncher.Views;
using OneLauncher.Views.Windows;
using OneLauncher.Views.Windows.WindowViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Codes;
internal class Game
{
    class LaunchException : Exception
    {
        public string InstanceName;
        public string InstanceId;
        public string MinecraftVersion;
        public List<string> ModList;
        public ModEnum ModType;
        public string SystemEnvironmentInformations;
        public string ProcessOutputs;
        public string FormerLaunchArgs;
#pragma warning disable CS8618 
        public LaunchException(GameData data, string gameDirectory, string output, int exitCode)
#pragma warning restore CS8618 
        { 
            InstanceName = data.Name;
            InstanceId = data.InstanceId;
            MinecraftVersion = data.VersionId;
            ModType = data.ModLoader;
            SystemEnvironmentInformations = 
                $"OSVersion:{Environment.OSVersion} UserName:{Environment.UserName} CPU:{Environment.CpuUsage.TotalTime}";
            ProcessOutputs = $"退出码：{exitCode}{Environment.NewLine}{output}{Environment.NewLine}" +
                LaunchDiagnostics.ReadOptionalLog(Path.Combine(gameDirectory, "logs", "latest.log"));
            FormerLaunchArgs = LaunchDiagnostics.ReadOptionalLog(Path.Combine(Init.BasePath, "LastLaunchArgs.bat"));
            ModList = new List<string>();
            var path = Path.Combine(data.InstancePath, "mods");
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path);
                if (files.Length > 0)
                {
                    ModList = files.ToList();
                }
            }
            else
                ModList = new List<string>();
        }
    }
    public static async Task EasyGameLauncher(UnitedCore unitedCore, GameData gameData,ServerInfo? serverInfo,bool useDebugMode)
    {
        using var gameLauncher = unitedCore.GameLaunchService.CreateLaunchService($"启动 {gameData.Name}");
        Action? startedHandler = null;
        Action<int>? closedHandler = null;
        Action<string>? outputHandler = null;
        var recentOutput = new Queue<string>();
        object outputLock = new();
        try
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("正在启动游戏..."));
            startedHandler = () =>
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("游戏已启动！"));
            closedHandler = (code) =>
            {
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("游戏已关闭！"));
                try
                {
                    if (code != 0)
                        //_=OlanExceptionWorker.ForOlanException(
                        throw new OlanException(
                             "游戏异常退出",
                             $"检测到游戏异常退出，代码：{code}{Environment.NewLine}建议尝试以调式模式启动或询问Agent以寻找异常原因{Environment.NewLine}点击重试以激活Agent调试",
                             OlanExceptionAction.Warning,
                             new LaunchException(gameData, gameLauncher.GameDirectory, string.Concat(recentOutput), code),
                             () => _ = EasyGameLauncher(unitedCore, gameData, serverInfo, true));
                }
                catch(OlanException ex)
                {
                    _ = OlanExceptionWorker.ForOlanException(ex);
                }
            };
            
            if (useDebugMode)
            {
                // 如果是调试模式，使用调试窗口
                new GameTasker().Show();
            }
            outputHandler = message =>
            {
                lock (outputLock)
                {
                    recentOutput.Enqueue(message);
                    while (recentOutput.Count > 200) recentOutput.Dequeue();
                }
                if (useDebugMode)
                    WeakReferenceMessenger.Default.Send(new GameMessage(message));
            };
            gameLauncher.GameStartedEvent += startedHandler;
            gameLauncher.GameClosedEvent += closedHandler;
            if (outputHandler is not null)
                gameLauncher.GameOutputEvent += outputHandler;
            await gameLauncher.Play(gameData, serverInfo);
        }
        #region 错误处理
        catch (OperationCanceledException)
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("已取消启动游戏。"));
        }
        catch (OlanException ex)
        {   
            await OlanExceptionWorker.ForOlanException(ex);
        }
        catch (UnauthorizedAccessException uex)
        {
            await OlanExceptionWorker.ForOlanException(
                        new OlanException("启动失败", $"没有权限访问游戏文件夹{Environment.NewLine}{uex}", OlanExceptionAction.Error, uex));
        }
        catch (FileNotFoundException fex)
        {
            await OlanExceptionWorker.ForOlanException(
                        new OlanException("启动失败", $"无法找到启动所需的文件{Environment.NewLine}{fex}", OlanExceptionAction.Error, fex));
        }
        catch (DirectoryNotFoundException fex)
        {
            await OlanExceptionWorker.ForOlanException(
                        new OlanException("启动失败", $"无法找到启动所需的文件夹{Environment.NewLine}{fex}", OlanExceptionAction.Error, fex));
        }
        catch (Exception ex)
        {
            await OlanExceptionWorker.ForOlanException(
                        new OlanException("启动失败", $"系统未安装Java或系统错误{Environment.NewLine}{ex}", OlanExceptionAction.Error, ex));
        }
        #endregion
        finally
        {
            if (startedHandler is not null)
                gameLauncher.GameStartedEvent -= startedHandler;
            if (closedHandler is not null)
                gameLauncher.GameClosedEvent -= closedHandler;
            if (outputHandler is not null)
                gameLauncher.GameOutputEvent -= outputHandler;
        }
    }
}
