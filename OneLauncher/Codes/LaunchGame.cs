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
        public string[] ModList;
        public ModEnum ModType;
        public string SystemEnvironmentInformations;
        public string ProcessOutputs;
        public LaunchException(GameData data) 
        { 
            InstanceName = data.Name;
            InstanceId = data.InstanceId;
            MinecraftVersion = data.VersionId;
            ModType = data.ModLoader;
            SystemEnvironmentInformations = 
                $"OSVersion:{Environment.OSVersion} UserName:{Environment.UserName} CPU:{Environment.CpuUsage.TotalTime}";
            ProcessOutputs = File.ReadAllText(Path.Combine(Init.GameRootPath,"logs","latest.log"));
            var path = Path.Combine(data.InstancePath, "mods");
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path);
                if (files.Length > 0)
                {
                    ModList = files;
                }
            }
            else
                ModList = Array.Empty<string>();
            
        }
    }
    public static async Task EasyGameLauncher(GameData gameData,ServerInfo? serverInfo,bool useDebugMode)
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("正在启动游戏..."));
            IGameLauncher gameLauncher = new GameLauncher();
            gameLauncher.GameStartedEvent += () =>
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("游戏已启动！"));
            gameLauncher.GameClosedEvent += (code) =>
            {
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("游戏已关闭！"));
                try
                {
                    if (code != 0)
                        //_=OlanExceptionWorker.ForOlanException(
                        throw new OlanException(
                             "游戏异常退出",
                             $"检测到游戏异常退出，代码：{code}{Environment.NewLine}建议尝试以调式模式启动或询问Gemini以寻找异常原因",
                             OlanExceptionAction.Warning,
                             new LaunchException(gameData),
                             () => _ = EasyGameLauncher(gameData, serverInfo, true));
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
                gameLauncher.GameOutputEvent += (m)
                    => WeakReferenceMessenger.Default.Send(new GameMessage(m));
            }
            await gameLauncher.Play(gameData, serverInfo);
        }
        #region 错误处理
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
    }
}

