using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.Account.Microsoft;
using OneLauncher.Core.Net.Account.Yggdrasil.ServiceProviders;
using OneLauncher.Views.ViewModels;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;
internal partial class UserModelLoginPaneViewModel : BaseViewModel
{
#if DEBUG
    public UserModelLoginPaneViewModel() { }
#endif
    private readonly MsalAuthenticator _accountManager;
    private readonly AccountManager ac;
    private readonly Action _onCloseCallback;
    public UserModelLoginPaneViewModel(MsalAuthenticator accountManager, AccountManager ma,Action onCloseCallback)
    {
        _accountManager = accountManager;
        ac = ma;
        _onCloseCallback = onCloseCallback;
    }
    [ObservableProperty]
    public bool _IsRYaLogin = false;
    [ObservableProperty]
    public bool _IsYaLogin = true;
    [ObservableProperty]
    public bool _IsMsaLogin = false;
    private ListBoxItem _whiceLoginType;
    public ListBoxItem WhiceLoginType
    {
        set
        {
            _whiceLoginType = value;
            if (value.Content == "离线账户")
            {
                Debug.WriteLine("离线登入");
                IsYaLogin = true;
                IsMsaLogin = false;
                IsRYaLogin = false;
            }
            if (value.Content == "微软账户")
            {
                Debug.WriteLine("微软登入");
                IsYaLogin = false;
                IsMsaLogin = true;
                IsRYaLogin = false;
            }
            if (value.Content == "外置登入")
            {
                Debug.WriteLine("外置登入");
                IsYaLogin = false;
                IsMsaLogin = false;
                IsRYaLogin = true;
            }
        }
        get
        {
            return _whiceLoginType;
        }
    }
    #region 外置登入
    [ObservableProperty] public string _RUserName;
    [ObservableProperty] public string _RPassword;
    [RelayCommand]
    public async Task RLogin()
    {
        if (string.IsNullOrEmpty(RUserName) || string.IsNullOrEmpty(RPassword))
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("用户名或密码不能为空！", NotificationType.Warning));
            return;
        }
        try
        {
            var um = await new LittleSkinAuthenticator().AuthenticateUseUserNameAndPasswordAsync(RUserName, RPassword);
            await ac.AddUser(um);
            WeakReferenceMessenger.Default.Send(new AccountPageDisplayListRefreshMessage());
            _onCloseCallback();
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已登入账号:{um.Name}"));
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex);
            return;
        }
    }
    #endregion
    #region 离线登入
    [ObservableProperty]
    public string _UserName;
    [RelayCommand]
    public void Done()
    {
        if (string.IsNullOrEmpty(UserName)) return;
        if (!Regex.IsMatch(UserName, @"^[a-zA-Z0-9_]+$"))
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("用户名包含非法字符！", Avalonia.Controls.Notifications.NotificationType.Warning));
            return;
        }
        ac.AddUser(new UserModel(
            UserID: Guid.NewGuid(),
            name: UserName,
            uuid: Guid.NewGuid()
        ));
        WeakReferenceMessenger.Default.Send(new AccountPageDisplayListRefreshMessage());
        _onCloseCallback();
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已添加账号:{UserName}"));
    }
    [RelayCommand]
    public void Back()
    {
        _onCloseCallback();
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("数据已销毁", Avalonia.Controls.Notifications.NotificationType.Information));
    }
    #endregion
    #region 微软登入
    [ObservableProperty] bool useWAMLogin = false;
    [RelayCommand]
    public Task LoginWithMicrosoft()
        => LoginWithMicrosoftHandle();
    private async Task LoginWithMicrosoftHandle(bool useWeb = false)
    {
        try
        {
            if (UseWAMLogin)
                _accountManager.FallbackToWeb();
            UserModel um;
#if WINDOWS
            if (Init.SystemType == SystemType.windows && !useWeb)
            {
                //throw new Exception("test");
                um =
                    await _accountManager.LoginNewAccountToGetMinecraftMojangAccessTokenUseWindowsWebAccountManger(
                        (MainWindow.mainwindow.TryGetPlatformHandle().Handle))
                    ?? throw new OlanException("认证失败", "无法认证你的微软账号");
            }
            else
#endif
            {
                um =
                    await _accountManager.LoginNewAccountToGetMinecraftMojangAccessTokenOnSystemBrowser()
                    ?? throw new OlanException("认证失败", "无法认证你的微软账号");
            }
            await ac.AddUser(um);
            using (var task = new MojangProfile(um))
                await task.GetSkinHeadImage();
            WeakReferenceMessenger.Default.Send(new AccountPageDisplayListRefreshMessage());
            _onCloseCallback();
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已登入账号:{um.Name}"));
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex);
            return;
        }
        catch (Exception ex)
        {
            // 如果操作系统不支持WAM可以尝试回退到网页登入
            await OlanExceptionWorker.ForUnknowException(ex,
                 () => 
                 {
                     // 让他重写赋值，回退到浏览器模式
                     _accountManager.FallbackToWeb();
                     _ = LoginWithMicrosoftHandle(true);
                 });
            return;
        }
    }
    #endregion
}
