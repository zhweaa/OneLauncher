using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OneLauncher.Core.Global;
using OneLauncher.Views.Panes.PaneViewModels;

namespace OneLauncher.ViewModel.PaneViewModels.Factories;
internal class NewGameDataPaneViewModelFactory
{
    private readonly GameDataManager _gameDataManager;
    private readonly VersionListManager _versionListManager;
    private readonly AccountManager _accountManager;
    public NewGameDataPaneViewModelFactory(UnitedCore unitedCore)
    {
        _versionListManager = unitedCore.VersionListManager;
        _gameDataManager = unitedCore.GameDataManager;
        _accountManager = unitedCore.AccountManager;
    }
    public NewGameDataPaneViewModel Create(Action onCloseCallback)
    {
        return new NewGameDataPaneViewModel(_versionListManager, _accountManager,_gameDataManager,onCloseCallback);
    }
    public NewGameDataPaneViewModel Create(UserVersion defaultBasicVerions, Action onCloseCallback)
    {
        return new NewGameDataPaneViewModel(_versionListManager, _accountManager, _gameDataManager, onCloseCallback, defaultBasicVerions);
    }
}
