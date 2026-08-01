using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace OneLauncher.Views.Controls;

public sealed class PaneHeader : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PaneHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string> BackLabelProperty =
        AvaloniaProperty.Register<PaneHeader, string>(nameof(BackLabel), "返回");

    public static readonly StyledProperty<ICommand?> BackCommandProperty =
        AvaloniaProperty.Register<PaneHeader, ICommand?>(nameof(BackCommand));

    public static readonly StyledProperty<object?> TrailingContentProperty =
        AvaloniaProperty.Register<PaneHeader, object?>(nameof(TrailingContent));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string BackLabel
    {
        get => GetValue(BackLabelProperty);
        set => SetValue(BackLabelProperty, value);
    }

    public ICommand? BackCommand
    {
        get => GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public object? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }
}
