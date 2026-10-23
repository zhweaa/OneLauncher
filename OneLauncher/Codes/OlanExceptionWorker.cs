using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Global;
using OneLauncher.Views;
using OneLauncher.Views.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Codes;

internal class OlanExceptionWorker
{
    public static Task ForOlanException(OlanException exception, Action? TryAgainFunction = null)
    {
        if(TryAgainFunction != null)
            exception.TryAgainFunction = TryAgainFunction;
        return Dispatcher.UIThread.InvokeAsync(async () => 
        {
            if (exception.Action == OlanExceptionAction.Warning)
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(exception.Message,NotificationType.Warning,title:exception.Title));
            if (exception.Action == OlanExceptionAction.Error)
                await new ExceptionTip(exception).ShowDialog(MainWindow.mainwindow);
            if (exception.Action == OlanExceptionAction.FatalError)
            {
                await new ExceptionTip(exception).ShowDialog(MainWindow.mainwindow);
                Environment.Exit(1);
            }
        });
    }
    public static Task ForUnknowException(Exception exception, Action TryAgainFunction = null)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await new ExceptionTip(new OlanException("出现未知错误",exception.ToString(),OlanExceptionAction.Error,exception,TryAgainFunction)).ShowDialog(MainWindow.mainwindow);
        });
    }
}
