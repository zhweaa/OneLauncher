using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Services;
using OneLauncher.Views;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;

internal static class ImportTaskRegressionTests
{
    internal static void Run(string? artifacts)
    {
        var manager = new TaskManager();
        var recipient = new object();
        int notifications = 0;
        WeakReferenceMessenger.Default.Register<MainWindowShowFlyoutMessage>(recipient, (_, _) => notifications++);
        try
        {
            var success = manager.CreateTask("PCL 导入：测试成功");
            int updates = 0;
            success.ProgressChanged += _ => Interlocked.Increment(ref updates);
            IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>? savedProgress = null;
            ImportTaskRunner.RunAsync(success, (progress, token) =>
            {
                savedProgress = progress;
                for (int index = 1; index <= 10000; index++)
                    progress.Report((DownProgress.DownAssets, 10000, index, "迁移资源对象"));
                progress.Report((DownProgress.Done, 1, 1, "登记完成"));
                Require(success.Status == TaskInfoStatus.Running && success.Progress < 100,
                    "Done 回调不能在导入方法返回前报告任务完成。");
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            Require(success.Status == TaskInfoStatus.Finished && success.Progress == 100, "成功导入未完成任务。");
            Require(manager.TaskQueue.Count == 1 && updates < 100, "逐文件进度没有合并到单个任务或未限频。");
            int updatesAtExit = updates;
            savedProgress!.Report((DownProgress.Meta, 0, 0, "迟到的回调"));
            Require(updates == updatesAtExit && success.Current == "导入完成", "结束后仍更新了任务进度。");
            Console.WriteLine("PASS: 10000 file reports / one task / throttled progress / no late updates");

            var failure = manager.CreateTask("PCL 导入：缺少依赖");
            var error = new InvalidDataException("缺少 Forge 运行文件 fmlcore.jar");
            ImportTaskRunner.RunAsync(failure, (_, _) => Task.FromException(error)).GetAwaiter().GetResult();
            Require(failure.Status == TaskInfoStatus.Failed && ReferenceEquals(failure.Exception, error), "失败原因没有保留在任务中心。");
            Console.WriteLine("PASS: import failure retains exception without a success state");

            var premature = manager.CreateTask("PCL 导入：登记失败");
            ImportTaskRunner.RunAsync(premature, (progress, _) =>
            {
                progress.Report((DownProgress.Done, 1, 1, "完成"));
                throw new IOException("登记失败");
            }).GetAwaiter().GetResult();
            Require(premature.Status == TaskInfoStatus.Failed && premature.Progress < 100, "提前 Done 隐藏了后续失败。");
            Console.WriteLine("PASS: premature Done followed by failure never becomes Finished");

            var canceled = manager.CreateTask("PCL 导入：已取消");
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task cancelRun = ImportTaskRunner.RunAsync(canceled, async (progress, token) =>
            {
                progress.Report((DownProgress.DownLibs, 20, 5, "迁移依赖库"));
                ready.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            });
            ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var cancelItem = new TaskInstanceItemViewModel(canceled);
            cancelItem.CancelTaskCommand.Execute(null);
            cancelRun.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Require(canceled.Status == TaskInfoStatus.Canceled && canceled.Exception == null && !cancelItem.CanCancel,
                "任务中心取消没有传播或被当作失败。");
            cancelItem.Dispose();
            Console.WriteLine("PASS: real task-center cancel command propagates to importer");

            var alreadyCanceled = manager.CreateTask("整合包导入：启动前取消");
            alreadyCanceled.Cancel();
            bool started = false;
            ImportTaskRunner.RunAsync(alreadyCanceled, (_, _) =>
            {
                started = true;
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            Require(!started && alreadyCanceled.Status == TaskInfoStatus.Canceled, "预先取消的导入仍开始执行。");

            var running = manager.CreateTask("整合包导入：迁移资源对象");
            var runningReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task run = ImportTaskRunner.RunAsync(running, async (progress, _) =>
            {
                progress.Report((DownProgress.DownAssets, 100, 42, "迁移资源对象"));
                runningReady.SetResult();
                await release.Task;
            });
            runningReady.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var center = new TaskCenterPaneViewModel(new UnitedCore { TaskManager = manager }, () => { });
            var window = new Window
            {
                Content = new TaskCenterPane { DataContext = center }, Width = 700, Height = 850
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Require(center.Tasks.Count == 6 && center.Tasks[0].Task == running, "任务中心没有显示真实导入任务。");
                Require(center.Tasks[0].Progress == 42 && center.Tasks[0].CurrentText.Contains("42/100"), "阶段计数和进度没有绑定到任务中心。");
                Require(center.Tasks.Single(item => item.Task == failure).CurrentText == error.Message, "错误详情没有显示。");
                if (artifacts != null)
                {
                    Directory.CreateDirectory(artifacts);
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(artifacts, "import-task-center.png"));
                }
            }
            finally
            {
                window.Close();
                release.TrySetResult();
                run.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                foreach (var item in center.Tasks) item.Dispose();
            }
            Require(notifications == 0, "导入仍发送了弹出通知。");
            Console.WriteLine("PASS: real task-center bindings / stage counts / errors / zero notifications");
            Console.WriteLine("PASS: pre-canceled import never starts");
            Console.WriteLine("PASS: all 6 import-task regression tests");
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
