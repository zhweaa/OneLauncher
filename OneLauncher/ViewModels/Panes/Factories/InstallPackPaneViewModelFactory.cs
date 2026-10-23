using OneLauncher.Core.Global;
using OneLauncher.Views.Panes.PaneViewModels;
using OneLauncher.Views.ViewModels;
using System;

namespace OneLauncher.ViewModel.PaneViewModels.Factories;

internal sealed class InstallPackPaneViewModelFactory
{
    private readonly UnitedCore _unitedCore;

    public InstallPackPaneViewModelFactory(UnitedCore unitedCore) => _unitedCore = unitedCore;

    public InstallPackPaneViewModel Create(ResourceItem item, Action onCloseCallback)
        => new(item, _unitedCore, onCloseCallback);
}
