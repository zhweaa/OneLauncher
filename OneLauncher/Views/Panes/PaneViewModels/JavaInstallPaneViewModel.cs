using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Views.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;

internal sealed record JavaProviderItem(JavaProvider Provider, string DisplayName, int MinimumVersion = 0);

internal partial class JavaInstallPaneViewModel : BaseViewModel
{
    private readonly JavaManager _javaManager;
    private readonly Action _onCloseCallback;
    private CancellationTokenSource? _cts;
    private static readonly JavaProviderItem[] AllJavaProviders =
    [
        new(JavaProvider.Adoptium, "Eclipse Adoptium"),
        new(JavaProvider.AzulZulu, "Azul Zulu"),
        new(JavaProvider.MicrosoftOpenJDK, "Microsoft OpenJDK", 11),
        new(JavaProvider.OracleGraalVM, "Oracle GraalVM", 21),
        new(JavaProvider.OracleJDK, "Oracle JDK")
    ];

    public JavaInstallPaneViewModel(JavaManager javaManager, Action onCloseCallback)
    {
        _javaManager = javaManager;
        _onCloseCallback = onCloseCallback;
        RefreshProviderOptions();
        RefreshInstalledState();
    }

    [ObservableProperty] private ObservableCollection<JavaProviderItem> _javaProviders = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(OverwriteCommand))]
    private JavaProviderItem? _selectedJavaProvider;
    [ObservableProperty] private ObservableCollection<int> _javaVersions = new([8, 11, 16, 17, 21, 24]);
    [ObservableProperty] private int _selectedJavaVersion = 21;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(OverwriteCommand))]
    private bool _isSelectedVersionInstalled;
    [ObservableProperty] private string _installedVersionsText = string.Empty;

    [ObservableProperty] private string _statusText = "等待安装";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(OverwriteCommand))]
    private bool _isInstalling;

    partial void OnSelectedJavaVersionChanged(int value)
    {
        RefreshProviderOptions();
        RefreshInstalledState();
    }

    private bool CanInstall() =>
        !IsInstalling && !IsSelectedVersionInstalled && SelectedJavaProvider is not null;

    private bool CanOverwrite() =>
        !IsInstalling && IsSelectedVersionInstalled && SelectedJavaProvider is not null;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        await InstallCore(false);
    }

    [RelayCommand(CanExecute = nameof(CanOverwrite))]
    private async Task Overwrite()
    {
        await InstallCore(true);
    }

    private async Task InstallCore(bool overwrite)
    {
        if (IsInstalling || SelectedJavaProvider is null)
            return;

        if (!overwrite && _javaManager.IsJavaInstalled(SelectedJavaVersion))
        {
            RefreshInstalledState();
            StatusText = $"Java {SelectedJavaVersion} 已经安装";
            return;
        }

        IsInstalling = true;
        ProgressValue = 0;
        StatusText = overwrite
            ? $"正在准备覆盖安装 Java {SelectedJavaVersion}"
            : $"正在准备 Java {SelectedJavaVersion}";
        CancellationTokenSource installCts = new();
        _cts = installCts;

        try
        {
            var progress = new Progress<string>(message =>
            {
                ProgressValue = Math.Min(28, ProgressValue + 1);
                StatusText = message;
            });

            await _javaManager.InstallJava(
                SelectedJavaVersion,
                SelectedJavaProvider.Provider,
                overwrite: overwrite,
                progress: progress,
                token: installCts.Token);

            ProgressValue = 28;
            StatusText = overwrite
                ? $"Java {SelectedJavaVersion} 覆盖安装完成，已更新配置"
                : $"Java {SelectedJavaVersion} 安装完成，已写入配置";
            RefreshInstalledState();
        }
        catch (OperationCanceledException)
        {
            StatusText = "安装已取消";
            RefreshInstalledState();
        }
        catch (OlanException ex)
        {
            StatusText = "安装失败";
            RefreshInstalledState();
            await OlanExceptionWorker.ForOlanException(ex);
        }
        catch (Exception ex)
        {
            StatusText = "安装失败";
            RefreshInstalledState();
            await OlanExceptionWorker.ForUnknowException(ex);
        }
        finally
        {
            _cts = null;
            installCts.Dispose();
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        _cts?.Cancel();

        _onCloseCallback();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsInstalling)
        {
            StatusText = "正在取消安装...";
            _cts?.Cancel();
            return;
        }

        _onCloseCallback();
    }

    private void RefreshProviderOptions()
    {
        JavaProvider? previousProvider = SelectedJavaProvider?.Provider;
        JavaProviders = new ObservableCollection<JavaProviderItem>(
            AllJavaProviders.Where(provider => SelectedJavaVersion >= provider.MinimumVersion));
        SelectedJavaProvider = JavaProviders.FirstOrDefault(provider => provider.Provider == previousProvider)
                               ?? JavaProviders.FirstOrDefault();
    }

    private void RefreshInstalledState()
    {
        IsSelectedVersionInstalled = _javaManager.IsJavaInstalled(SelectedJavaVersion);
        int[] installedVersions = _javaManager.GetInstalledVersions().ToArray();
        InstalledVersionsText = installedVersions.Length == 0
            ? "尚未配置 Java"
            : $"已配置版本：{string.Join(", ", installedVersions)}";
    }
}
