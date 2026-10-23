using Avalonia;
using Avalonia.Controls;
using System.ComponentModel;

namespace OneLauncher.Views.Controls;

/// <summary>由 Button.Flyout 打开，使用双向绑定通知编辑开始并在保存后关闭。</summary>
public sealed class SettingsFlyout : Flyout
{
    public static readonly StyledProperty<bool> IsEditingProperty =
        AvaloniaProperty.Register<SettingsFlyout, bool>(nameof(IsEditing));
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<SettingsFlyout, bool>(nameof(IsBusy));

    public bool IsEditing
    {
        get => GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        if (!args.Cancel)
            SetCurrentValue(IsEditingProperty, true);
    }

    protected override void OnClosed()
    {
        SetCurrentValue(IsEditingProperty, false);
        base.OnClosed();
    }

    protected override void OnClosing(CancelEventArgs args)
    {
        base.OnClosing(args);
        if (IsBusy && IsEditing)
            args.Cancel = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEditingProperty && !IsEditing && IsOpen)
            Hide();
    }
}
