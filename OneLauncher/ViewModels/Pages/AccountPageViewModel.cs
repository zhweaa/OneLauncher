using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Account.Microsoft;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Interfaces;
using OneLauncher.ViewModel.Page;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Views.Panes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
internal partial class AccountPageViewModel : BaseViewModel, IPageHost, IListTemplate<UserItem>
{
    private AccountManager _accountManager => _unitedCore.AccountManager;
    private MsalAuthenticator _msalAuthenticator => _unitedCore.MsalAuthenticator;
    private readonly UnitedCore _unitedCore;
    private readonly DBManager _dbManager;
    public bool ShowIdentifiers => _dbManager.Data.OlanSettings.ShowIdentifiers;
    private UserModelLoginPaneViewModelFactory _userModelLoginPaneFactory;
    // 刷新
    public Task RefList()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildList();
            return Task.CompletedTask;
        }
        return Dispatcher.UIThread.InvokeAsync(RebuildList).GetTask();
    }

    private void RebuildList()
    {
        // 在刷新列表时，判断每一项是否为默认用户
        UserModel? defaultUser = _accountManager.GetDefaultUser();

        Items = new ObservableCollection<UserItem>(_accountManager.GetAllUsers()
            .Select(user => new UserItem()
            {
                um = user,
                HeadImg = (user.UserType == AccountType.Msa && File.Exists(Path.Combine(Init.BasePath, "playerdata", "body", $"{user.uuid}.png")))
                    ? new Bitmap(Path.Combine(Init.BasePath, "playerdata", "body", $"{user.uuid}.png"))
                    : new Bitmap(AssetLoader.Open(new Uri("avares://OneLauncher/Assets/Imgs/steve.png"))),
                // 在创建 UserItem 时，就设置好 IsDefault 属性
                IsDefault = (defaultUser != null && user.uuid == defaultUser.uuid)
            }));
    }
    [RelayCommand]
    public async Task Refresh()
    {
        try
        {
            foreach (var user in Items)
            {
                if(user.um.UserType == AccountType.Msa)
                    using (var task = new MojangProfile(user.um))
                        await task.GetSkinHeadImage();
            }
            await RefList();
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
    public AccountPageViewModel(
        DBManager dbManager,
        UnitedCore unitedCore,
        UserModelLoginPaneViewModelFactory userModelLoginPaneFactory
        )
    {
        _dbManager = dbManager;
        this._unitedCore = unitedCore;
        this._userModelLoginPaneFactory = userModelLoginPaneFactory;
#if DEBUG
        if (Design.IsDesignMode)
            Items = new ObservableCollection<UserItem>()
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
                _ = RefList();
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
            WeakReferenceMessenger.Default.Register<AccountPageDisplayListRefreshMessage>(this, (re, message) => _ = RefList());
            WeakReferenceMessenger.Default.Register<AccountPageViewModel, IdentifierVisibilityChangedMessage>(
                this, static (recipient, _) => Dispatcher.UIThread.Post(
                    () => recipient.OnPropertyChanged(nameof(ShowIdentifiers))));
        }
    }
    [ObservableProperty]
    private ObservableCollection<UserItem> items = new();
    [ObservableProperty]
    private bool _isPaneShow;
    [ObservableProperty]
    private UserControl? _paneContent;

    [RelayCommand]
    private void NewUserModel()
    {
        PaneContent = new UserModelLoginPane()
        { DataContext = _userModelLoginPaneFactory.Create(() => IsPaneShow = false) };
        IsPaneShow = true;
    }
    [RelayCommand]
    private void SkinManger(UserModel userModel)
    {
        PaneContent = new SkinMangerPane(this, userModel, () => IsPaneShow = false);
        IsPaneShow = true;
    }
    
    [RelayCommand]
    private async Task SetDefault(UserItem user)
    {
        Items.Select(x => x.IsDefault = false);
        user.IsDefault = true;
        _accountManager.SetDefault(user.um.UserID);
        await RefList();
        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已将默认用户模型设置为{user.um.Name}"));
        //MainWindow.mainwindow.ShowFlyout($"已将默认用户模型设置为{user.um.Name}");
    }
    [RelayCommand]
    private async Task DeleteUser(UserModel user)
    {
        try
        {
            if (user.UserType == AccountType.Msa)
                _msalAuthenticator.RemoveAccount(
                    Tools.UseAccountIDToFind(user.AccountID).Result);
            _accountManager.RemoveUser(user.UserID);
            await RefList();
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已移除用户模型{user.Name}"));
            //MainWindow.mainwindow.ShowFlyout($"已移除用户模型{user.Name}", true);
        }
        catch (OlanException ex) { 
            await OlanExceptionWorker.ForOlanException(ex);
        }
    }
}
