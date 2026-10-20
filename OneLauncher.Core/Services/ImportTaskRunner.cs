using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.Services;

/// <summary>导入的进度及终态只交给任务中心，不产生通知或错误弹窗。</summary>
public static class ImportTaskRunner
{
    public static async Task RunAsync(TaskInstance task,
        Func<IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>, CancellationToken, Task> import)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(import);
        CancellationToken token = task.Token?.Token
            ?? throw new ArgumentException("导入任务必须由 TaskManager.CreateTask 创建。", nameof(task));
        var progress = new TaskProgress(task, token);
        try
        {
            progress.Report((DownProgress.Meta, 0, 0, "准备导入..."));
            // 复制和校验不占用 UI 线程，进度回调也不向同步上下文逐文件排队。
            await Task.Run(() => import(progress, token), token).ConfigureAwait(false);
            progress.Finish();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            progress.Finish(canceled: true);
        }
        catch (Exception exception)
        {
            // 失败保留在 TaskInstance.Exception 中，任务中心负责显示；不能伪造完成。
            progress.Finish(exception);
        }
    }

    private sealed class TaskProgress(TaskInstance task, CancellationToken token)
        : IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>
    {
        private readonly object _sync = new();
        private DownProgress? _stage;
        private long _lastUpdate;
        private int _completed;
        private bool _closed;

        public void Report((DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName) value)
        {
            lock (_sync)
            {
                if (_closed || task.Status != TaskInfoStatus.Running || token.IsCancellationRequested)
                    return;
                bool newStage = _stage != value.Title;
                if (newStage) _completed = 0;
                int total = Math.Max(0, value.AllFiles);
                if (!newStage && total > 0 && value.DownedFiles <= _completed)
                    return;
                _completed = Math.Max(_completed, Math.Clamp(value.DownedFiles, 0, total));
                long now = Environment.TickCount64;
                // 阶段切换及阶段末尾立即显示，其余高频文件回调最多每 100 ms 刷新一次。
                if (!newStage && now - _lastUpdate < 100 && (total == 0 || _completed < total) && value.Title != DownProgress.Done)
                    return;
                _stage = value.Title;
                _lastUpdate = now;
                string stage = value.Title switch
                {
                    DownProgress.Meta => "准备 / 迁移配置",
                    DownProgress.DownMain => "主程序",
                    DownProgress.DownLibs => "依赖库",
                    DownProgress.DownAssets => "资源对象",
                    DownProgress.DownAndInstModFiles => "Mods、配置和存档",
                    DownProgress.DownLog4j2 => "日志配置",
                    DownProgress.Verify => "文件校验",
                    _ => "正在完成导入"
                };
                string count = total > 0 ? $" {_completed}/{total}" : "";
                double percentage = total > 0 ? Math.Min(99, (double)_completed / total * 100) : 0;
                task.SetProgress($"{stage}{count}：{value.DowingFileName}", percentage);
            }
        }

        public void Finish(Exception? exception = null, bool canceled = false)
        {
            lock (_sync)
            {
                _closed = true;
                if (task.Status != TaskInfoStatus.Running) return;
                if (canceled || token.IsCancellationRequested)
                    task.Cancel();
                else if (exception != null)
                    task.SetFailed(exception);
                else
                {
                    task.SetProgress("导入完成", 100);
                    task.SetDone();
                }
            }
        }
    }
}
