namespace OneLauncher.Core.Downloader;

// 下载计数在工作线程同步更新，UI 调用方自行派发显示，避免完成后仍有排队计数回调。
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
