using Avalonia.Platform;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Net.ConnectToolPower;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels.Factories;

internal class PowerPlayPaneViewModelFactory
{
    private readonly GameDataManager _gameDataManager;
    public PowerPlayPaneViewModelFactory(GameDataManager gameDataManager)
    {
        _gameDataManager = gameDataManager;
    }
    public async Task<PowerPlayPaneViewModel> CreateAsync(Action? onCloseCallback = null)
    {
        var mctPower = await MCTPower.InitializationAsync();
        var connectService = new P2PMode(mctPower);
        var viewModel = new PowerPlayPaneViewModel(connectService, mctPower, _gameDataManager, onCloseCallback);
        return viewModel;
    }
}
