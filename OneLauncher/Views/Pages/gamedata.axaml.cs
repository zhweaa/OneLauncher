using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Views.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OneLauncher.Views;

public partial class gamedata : UserControl
{
    public gamedata()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<GameDataTagCreatedMessage>(this, static (recipient, _) =>
        {
            var view = (gamedata)recipient;
            Dispatcher.UIThread.Post(() =>
                FlyoutBase.GetAttachedFlyout(view.TagFilterComboBox)?.Hide());
        });
    }

    private void TagFilterComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TagFilterComboBox.SelectedItem is not GameDataTagItem { IsCreateAction: true }) return;

        TagFilterComboBox.SelectedItem = null;
        Dispatcher.UIThread.Post(
            () => FlyoutBase.ShowAttachedFlyout(TagFilterComboBox),
            DispatcherPriority.Input);
    }

    private void CloseCreateTagFlyout_OnClick(object? sender, RoutedEventArgs e) =>
        FlyoutBase.GetAttachedFlyout(TagFilterComboBox)?.Hide();
}
