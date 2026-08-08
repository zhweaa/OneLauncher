using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Net.Server;
using System;

namespace OneLauncher.Views.Panes.PaneViewModels.Factories;

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
