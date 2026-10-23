using CommunityToolkit.Mvvm.ComponentModel;
using OneLauncher.ViewModel.Page;

namespace OneLauncher.Views.ViewModels;

internal enum HomeSection
{
    News,
    TaskCenter,
    PowerPlay,
    Import
}

/// <summary>设置页中主页板块浮窗的编辑副本，保存前不写入配置。</summary>
internal partial class HomeSectionVisibilityItem : BaseViewModel
{
    public HomeSection Section { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool isVisible;

    public HomeSectionVisibilityItem(HomeSection section, string displayName, bool isVisible)
    {
        Section = section;
        DisplayName = displayName;
        this.isVisible = isVisible;
    }
}
