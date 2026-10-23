using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.DefaultComponent.Server;
using System;
using OneLauncher.Views.Panes.PaneViewModels;

namespace OneLauncher.ViewModel.PaneViewModels.Factories;

internal sealed class EditServerPaneViewModelFactory
{
    private readonly DBManager _dbManager;

    public EditServerPaneViewModelFactory(DBManager dbManager)
    {
        _dbManager = dbManager;
    }

    public EditServerPaneViewModel Create(
        ServerEntry serverEntry,
        Action onCloseCallback,
        Action onServerInfoUpdated,
        Action onEditGameData)
    {
        return new EditServerPaneViewModel(
            _dbManager,
            serverEntry,
            onCloseCallback,
            onServerInfoUpdated,
            onEditGameData);
    }
}
