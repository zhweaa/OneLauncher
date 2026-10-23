using OneLauncher.Core.DefaultComponent.Account.Microsoft;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Global;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OneLauncher.Views.Panes.PaneViewModels;

namespace OneLauncher.ViewModel.PaneViewModels.Factories;

internal class UserModelLoginPaneViewModelFactory
{
    private readonly MsalAuthenticator _msaManager;
    private readonly AccountManager _accountManager;
    public UserModelLoginPaneViewModelFactory(UnitedCore unitedCore)
    {
        _msaManager = unitedCore.MsalAuthenticator;
        _accountManager = unitedCore.AccountManager;
    }
    public UserModelLoginPaneViewModel Create(Action onCloseCallback)
    {
        return new UserModelLoginPaneViewModel(_msaManager, _accountManager,onCloseCallback);
    }
}
