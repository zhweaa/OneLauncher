using Avalonia;
using Avalonia.Controls;

namespace OneLauncher.Views.Controls;

public sealed class PageShell : ContentControl
{
    public static readonly StyledProperty<object?> PaneContentProperty =
        AvaloniaProperty.Register<PageShell, object?>(nameof(PaneContent));

    public static readonly StyledProperty<bool> IsPaneOpenProperty =
        AvaloniaProperty.Register<PageShell, bool>(nameof(IsPaneOpen));

    public static readonly StyledProperty<double> OpenPaneLengthProperty =
        AvaloniaProperty.Register<PageShell, double>(nameof(OpenPaneLength), 650d);

    public object? PaneContent
    {
        get => GetValue(PaneContentProperty);
        set => SetValue(PaneContentProperty, value);
    }

    public bool IsPaneOpen
    {
        get => GetValue(IsPaneOpenProperty);
        set => SetValue(IsPaneOpenProperty, value);
    }

    public double OpenPaneLength
    {
        get => GetValue(OpenPaneLengthProperty);
        set => SetValue(OpenPaneLengthProperty, value);
    }
}
