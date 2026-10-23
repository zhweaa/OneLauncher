using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Views.Panes.PaneViewModels;
using OneLauncher.Views.ViewModels;
using System;

namespace OneLauncher.ViewModel.PaneViewModels.Factories;

internal class InstallModPaneViewModelFactory
{
    private readonly GameDataManager _gameDataManager;
    private readonly UnitedCore _unitedCore;
    private readonly DBManager _configManager;

    public InstallModPaneViewModelFactory(UnitedCore unitedCore, DBManager configManager)
    {
        _gameDataManager = unitedCore.GameDataManager;
        _unitedCore = unitedCore;
        _configManager = configManager;
    }

    public InstallModPaneViewModel Create(ResourceItem item, Action onCloseCallback)
        => new(item, _gameDataManager, _unitedCore, _configManager, onCloseCallback);
}

