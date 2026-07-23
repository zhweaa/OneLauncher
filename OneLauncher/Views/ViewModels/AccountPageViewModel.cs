using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.Account.Microsoft;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels.Factories;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OneLauncher.Views.ViewModels;
internal class AccountPageDisplayListRefreshMessage { }
internal partial class UserItem
{
    public UserModel um { get; set; }
    public Bitmap HeadImg { get; set; } = new Bitmap(AssetLoader.Open(new Uri("avares://OneLauncher/Assets/Imgs/steve.png")));
    public bool IsDefault { get; set; }
    public bool IsNotDefault => !IsDefault;
    public bool IsMsaUser => um.UserType == AccountType.Msa;
}
internal partial class AccountPageViewModel : BaseViewModel
{
    private AccountManager _accountManager;
    private MsalAuthenticator _msalAuthenticator;
    private UserModelLoginPaneViewModelFactory _userModelLoginPaneFactory;
    // 刷新
    public void RefList()
    {
        // 在刷新列表时，判断每一项是否为默认用户
        UserModel? defaultUser = _accountManager.GetDefaultUser();

        UserModelList = _accountManager.GetAllUsers()
            .Select(user => new UserItem()
            {
                um = user,
                HeadImg = (user.UserType == AccountType.Msa && File.Exists(Path.Combine(Init.BasePath, "playerdata", "body", $"{user.uuid}.png")))
                    ? new Bitmap(Path.Combine(Init.BasePath, "playerdata", "body", $"{user.uuid}.png"))
                    : new Bitmap(AssetLoader.Open(new Uri("avares://OneLauncher/Assets/Imgs/steve.png"))),
                // 在创建 UserItem 时，就设置好 IsDefault 属性
                IsDefault = (defaultUser != null && user.uuid == defaultUser.uuid)
            }).ToList();
    }
    [RelayCommand]
    public async Task Refresh()
    {
        try
        {
            foreach (var user in UserModelList)
            {
                using (var task = new MojangProfile(user.um))
                    await task.GetSkinHeadImage();
            }
            RefList();
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage("刷新完毕", Avalonia.Controls.Notifications.NotificationType.Success));
        }
        catch (OlanException oex)
        {
            await OlanExceptionWorker.ForOlanException(oex);
        }
        catch (Exception ex) { 
            await OlanExceptionWorker.ForUnknowException(ex);
        }
    }
    public AccountPageViewModel(AccountManager manager,MsalAuthenticator msalAuthenticator, UserModelLoginPaneViewModelFactory userModelLoginPaneFactory)
    {
        this._userModelLoginPaneFactory = userModelLoginPaneFactory;
        this._accountManager = manager;
        this._msalAuthenticator = msalAuthenticator;
#if DEBUG
        if (Design.IsDesignMode)
            UserModelList = new List<UserItem>()
            {
                new UserItem()
                {
                    um = new UserModel(new Guid(),"steve",new Guid(UserModel.nullToken))

                }
            };
        else
#endif
        {
            try
            {
                RefList();
            }
            catch (NullReferenceException ex)
            {
                throw new OlanException(
                    "内部异常",
                    "配置文件特定部分账户部分为空，这可能是新版和旧版配置文件不兼容导致的",
                    OlanExceptionAction.FatalError,
                    ex,
                   () =>
                   {
                       File.Delete(Path.Combine(Init.BasePath, "config.json"));
                       Init.Initialize();
                   }
                    );
            }
            WeakReferenceMessenger.Default.Register<AccountPageDisplayListRefreshMessage>(this, (re, message) => RefList());
        }
    }
    [ObservableProperty]
    public List<UserItem> _UserModelList;
    [ObservableProperty]
    private bool _IsPaneShow= false;
    [ObservableProperty]
    public UserControl _AccountPane;

    [RelayCommand]
    private void NewUserModel()
    {
        IsPaneShow = true;
        AccountPane = new UserModelLoginPane()
        { DataContext = _userModelLoginPaneFactory.Create(() => IsPaneShow = false) };
    }
    [RelayCommand]
    private void SkinManger(UserModel userModel)
    {
        IsPaneShow = true;
        AccountPane = new SkinMangerPane(this, userModel, () => IsPaneShow = false);
    }
    
    [RelayCommand]
    private void SetDefault(UserItem user)
    {
        UserModelList.Select(x => x.IsDefault = false);
        user.IsDefault = true;
        _accountManager.SetDefault(user.um.UserID);
        RefList();
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已将默认用户模型设置为{user.um.Name}"));
        //MainWindow.mainwindow.ShowFlyout($"已将默认用户模型设置为{user.um.Name}");
    }
    [RelayCommand]
    private void DeleteUser(UserModel user)
    {
        try
        {
            if (user.UserType == AccountType.Msa)
                _msalAuthenticator.RemoveAccount(
                    Tools.UseAccountIDToFind(user.AccountID).Result);
            _accountManager.RemoveUser(user.UserID);
            RefList();
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已移除用户模型{user.Name}"));
            //MainWindow.mainwindow.ShowFlyout($"已移除用户模型{user.Name}", true);
        }
        catch (OlanException ex) { 
            OlanExceptionWorker.ForOlanException(ex);
        }
    }
}
