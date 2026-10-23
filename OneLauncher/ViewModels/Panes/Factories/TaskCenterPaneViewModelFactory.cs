using OneLauncher.Core.Global;
using OneLauncher.Views.Panes.PaneViewModels;
using System;

namespace OneLauncher.ViewModel.PaneViewModels.Factories;

internal sealed class TaskCenterPaneViewModelFactory
{
    private readonly UnitedCore _unitedCore;

    public TaskCenterPaneViewModelFactory(UnitedCore unitedCore)
    {
        _unitedCore = unitedCore;
    }

    public TaskCenterPaneViewModel Create(Action onCloseCallback)
        => new(_unitedCore, onCloseCallback);
}
