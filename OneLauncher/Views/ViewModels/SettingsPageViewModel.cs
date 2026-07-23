using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;
using OneLauncher.Views.Converters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Views.ViewModels;
internal partial class SettingsPageViewModel : BaseViewModel
{
    // 将 manager 重命名为 _dbManger 以遵循常见的私有字段命名约定
    private readonly DBManager _dbManger;
    private readonly JavaManager _javaManager;
    public string Version => Init.ApplicationVersoin;
    #region 启动参数优化选项
    [ObservableProperty]
    public bool isM1, isM2, isM3;
    partial void OnIsM1Changed(bool value)
    {
        if (!value) return; // 避免在取消选中时也执行
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.MinecraftJvmArguments = JvmArguments.CreateFromMode(OptimizationMode.Conservative);
        _dbManger.Save();
    }
    partial void OnIsM2Changed(bool value)
    {
        if (!value) return;
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.MinecraftJvmArguments = JvmArguments.CreateFromMode(OptimizationMode.Standard);
        _dbManger.Save();
    }
    partial void OnIsM3Changed(bool value)
    {
        if (!value) return;
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.MinecraftJvmArguments = JvmArguments.CreateFromMode(OptimizationMode.Aggressive);
        _dbManger.Save();
    }
    [RelayCommand]
    private void ReleaseMemory()
    {
        Process.Start(new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = Environment.ProcessPath,
            Arguments = "--releaseMemory"
#if WINDOWS
            ,
            Verb = "runas" // 请求管理员权限
#endif
        });
    }
    #endregion

    #region 下载选项
    [ObservableProperty]
    public int _MaxDownloadThreadsValue;
    partial void OnMaxDownloadThreadsValueChanged(int value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.MaximumDownloadThreads = value;
        _dbManger.Save();
    }

    [ObservableProperty]
    public int _MaxSha1ThreadsValue;
    partial void OnMaxSha1ThreadsValueChanged(int value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.MaximumSha1Threads = value;
        _dbManger.Save();
    }

    [ObservableProperty]
    public bool _IsSha1Enabled;
    partial void OnIsSha1EnabledChanged(bool value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        // 使用注入的实例
        _dbManger.Data.OlanSettings.IsSha1Enabled = value;
        _dbManger.Save();
    }

    [ObservableProperty]
    public DownloadSourceStrategy[] downloadSources =
        [
        DownloadSourceStrategy.OfficialOnly,
        DownloadSourceStrategy.RaceWithBmcl,
        DownloadSourceStrategy.RaceWithOlan
        ];
    [ObservableProperty]
    public DownloadSourceStrategy _SelectedDownloadSource = DownloadSourceStrategy.OfficialOnly;
    partial void OnSelectedDownloadSourceChanged(DownloadSourceStrategy value)
    {
#if DEBUG
        if (Design.IsDesignMode)
            return;
#endif
        _dbManger.Data.OlanSettings.DownloadMinecraftSourceStrategy = value;
        _dbManger.Save();
    }

    #endregion
    [RelayCommand]
    public void OpenGithub()
    {
        Tools.OpenWebsite(Init.ProjectWebsite);
    }
    [ObservableProperty]
    UserControl _paneContent;
    [ObservableProperty] bool _isPaneShow;

    [RelayCommand]
    private void OpenJavaInstallPane()
    {
        PaneContent = new JavaInstallPane
        {
            DataContext = new JavaInstallPaneViewModel(_javaManager, () => IsPaneShow = false)
        };
        IsPaneShow = true;
    }

    // 构造函数接收正确的 DBManger 类型
    public SettingsPageViewModel(DBManager configManager, JavaManager javaManager)
    {
        this._dbManger = configManager;
        this._javaManager = javaManager;
#if DEBUG
        if (Design.IsDesignMode)
        {
            MaxDownloadThreadsValue = 24;
            MaxSha1ThreadsValue = 24;
            IsSha1Enabled = true;
            return; // 在设计模式下提前返回
        }
#endif
        // else 块不再需要，因为非 DEBUG 模式下总会执行下面的代码

        try
        {
            // 使用注入的实例来初始化属性
            switch (_dbManger.Data.OlanSettings.MinecraftJvmArguments.mode)
            {
                case OptimizationMode.Conservative:
                    IsM1 = true;
                    break;
                case OptimizationMode.Standard:
                    IsM2 = true;
                    break;
                case OptimizationMode.Aggressive:
                    IsM3 = true;
                    break;
            }
            MaxDownloadThreadsValue = _dbManger.Data.OlanSettings.MaximumDownloadThreads;
            MaxSha1ThreadsValue = _dbManger.Data.OlanSettings.MaximumSha1Threads;
            IsSha1Enabled = _dbManger.Data.OlanSettings.IsSha1Enabled;
            SelectedDownloadSource = _dbManger.Data.OlanSettings.DownloadMinecraftSourceStrategy;
        }
        catch (NullReferenceException ex)
        {
            // 异常处理中的静态调用暂时保留，因为这是更深层次的重构问题
            throw new OlanException(
                "内部异常",
                "配置文件特定部分设置部分为空，这可能是新版和旧版配置文件不兼容导致的",
                OlanExceptionAction.FatalError,
                ex,
               () =>
               {
                   File.Delete(Path.Combine(Init.BasePath, "config.json"));
                   Init.Initialize(); // 注意：这个静态调用最终也应被移除
               }
            );
        }
    }
}
