using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Views.Panes.PaneViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.ViewModel.PaneViewModels.Factories;

internal class DownloadPaneViewModelFactory
{
    private readonly UnitedCore _unitedCore;

    public DownloadPaneViewModelFactory(UnitedCore unitedCore)
    {
        _unitedCore = unitedCore;
    }

    public DownloadPaneViewModel Create(VersionBasicInfo version,Action onCloseCallback)
    {
        return new DownloadPaneViewModel(version, _unitedCore.GameDataManager, _unitedCore, onCloseCallback);
    }
}
