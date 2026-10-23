using CommunityToolkit.Mvvm.ComponentModel;
using OneLauncher.ViewModel.Page;

namespace OneLauncher.Views.ViewModels;

internal enum NavigationPage
{
    Version,
    GameData,
    Server,
    Download,
    ModsBrowser,
    Account
}

internal partial class NavigationVisibilityItem : BaseViewModel
{
    public NavigationPage Page { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool isVisible;

    public NavigationVisibilityItem(NavigationPage page, string displayName, bool isVisible)
    {
        Page = page;
        DisplayName = displayName;
        this.isVisible = isVisible;
    }
}
