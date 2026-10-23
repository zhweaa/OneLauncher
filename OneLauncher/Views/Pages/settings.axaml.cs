using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using OneLauncher.Views.ViewModels;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
namespace OneLauncher.Views;

public partial class settings : UserControl
{
    int ci = 0;
    public settings()
    {
        InitializeComponent();
    }

    private void TextBlock_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        Debug.WriteLine("[OneLauncher.Views.settings.TextBlock_Tapped] 彩蛋按钮触发");
        ci++;
        if (ci >= 5)
        {
            Debug.WriteLine("[OneLauncher.Views.settings.TextBlock_Tapped] 已触发彩蛋");
            Task.Run(() =>
            {
                while(true)
                {
                    int rgb1 = Random.Shared.Next(0, 256);
                    int rgb2 = Random.Shared.Next(0, 256);
                    int rgb3 = Random.Shared.Next(0, 256);
                    Debug.WriteLine($"[OneLauncher.Views.settings.TextBlock_Tapped] 颜色：rgb({rgb1},{rgb2},{rgb3})");
                    Dispatcher.UIThread.Post(() =>
                    {
                        MainWindow.mainwindow.Background = new SolidColorBrush(Color.FromRgb((byte)rgb1, (byte)rgb2, (byte)rgb3));
                    });
                    Thread.Sleep(500);
                }
            });
        }
    }
}