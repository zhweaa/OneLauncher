using Avalonia.Platform;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Global;
using OneLauncher.Core.DefaultComponent.ConnectToolPower;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OneLauncher.Views.Panes.PaneViewModels;

namespace OneLauncher.ViewModel.PaneViewModels.Factories;

internal class PowerPlayPaneViewModelFactory
{
    private readonly GameDataManager _gameDataManager;
    private readonly UnitedCore _unitedCore;
    public PowerPlayPaneViewModelFactory(UnitedCore unitedCore)
    {
        _gameDataManager = unitedCore.GameDataManager;
        _unitedCore = unitedCore;
    }
    public async Task<PowerPlayPaneViewModel> CreateAsync(Action? onCloseCallback = null)
    {
        var mctPower = await MCTPower.InitializationAsync();
        var connectService = new P2PMode(mctPower);
        var viewModel = new PowerPlayPaneViewModel(connectService, mctPower, _gameDataManager, _unitedCore, onCloseCallback);
        return viewModel;
    }
}
