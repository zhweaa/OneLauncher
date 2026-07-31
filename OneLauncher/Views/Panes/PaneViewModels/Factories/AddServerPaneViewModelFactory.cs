using OneLauncher.Core.Global.ModelDataMangers;
using System;

namespace OneLauncher.Views.Panes.PaneViewModels.Factories;

internal sealed class AddServerPaneViewModelFactory
{
    private readonly DBManager _dbManager;
    private readonly GameDataManager _gameDataManager;

    public AddServerPaneViewModelFactory(
        DBManager dbManager,
        GameDataManager gameDataManager)
    {
        _dbManager = dbManager;
        _gameDataManager = gameDataManager;
    }

    public AddServerPaneViewModel Create(Action onCloseCallback)
    {
        return new AddServerPaneViewModel(
            _dbManager,
            _gameDataManager,
            onCloseCallback);
    }
}
