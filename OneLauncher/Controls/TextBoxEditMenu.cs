using System;
using Avalonia;
using Avalonia.Controls;

namespace OneLauncher.Views.Controls;

/// <summary>Creates a separate, statically wired editing menu for each TextBox.</summary>
public sealed class TextBoxEditMenu : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBoxEditMenu, TextBox, bool>("Enabled");

    static TextBoxEditMenu()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((textBox, change) =>
        {
            if (change.GetNewValue<bool>())
            {
                // Respect explicitly supplied menus. No DataContext/Popup ancestor lookup.
                if (textBox.ContextMenu is null)
                    textBox.SetCurrentValue(Control.ContextMenuProperty, new EditingContextMenu(textBox));
            }
            else if (textBox.ContextMenu is EditingContextMenu)
            {
                textBox.SetCurrentValue(Control.ContextMenuProperty, null);
            }
        });
    }

    public static bool GetEnabled(TextBox textBox) => textBox.GetValue(EnabledProperty);
    public static void SetEnabled(TextBox textBox, bool value) => textBox.SetValue(EnabledProperty, value);

    private sealed class EditingContextMenu : ContextMenu
    {
        protected override Type StyleKeyOverride => typeof(ContextMenu);

        public EditingContextMenu(TextBox textBox)
        {
            var copy = new MenuItem { Header = "复制" };
            var cut = new MenuItem { Header = "剪切" };
            var paste = new MenuItem { Header = "粘贴" };
            var selectAll = new MenuItem { Header = "全选" };
            copy.Click += (_, _) => textBox.Copy();
            cut.Click += (_, _) => textBox.Cut();
            paste.Click += (_, _) => textBox.Paste();
            selectAll.Click += (_, _) => textBox.SelectAll();
            ItemsSource = new Control[] { copy, cut, paste, new Separator(), selectAll };

            void Refresh()
            {
                // Avalonia 11 updates CanCopy/CanCut/CanPaste on selection/focus
                // changes, not every IsReadOnly/PasswordChar change.
                var canCopy = textBox.PasswordChar == default && !string.IsNullOrEmpty(textBox.SelectedText);
                copy.IsEnabled = canCopy;
                cut.IsEnabled = canCopy && !textBox.IsReadOnly;
                paste.IsEnabled = !textBox.IsReadOnly;
                selectAll.IsEnabled = !string.IsNullOrEmpty(textBox.Text);
            }
            Opening += (_, _) => Refresh();
            // Programmatic Open() bypasses Opening in Avalonia 11.
            Opened += (_, _) => Refresh();
        }
    }
}
