using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource;

/// <summary>
/// Mod 与光影包共用的文件下载、已存在文件检查和可选 SHA1 校验流程。
/// </summary>
internal static class ResourceFileInstaller
{
    public static async Task InstallAsync(
        List<NdDowItem> allFiles,
        IProgress<(long AllFiles, long DownedFiles, double Percentage, string DowingFileName)>? progress,
        string resourceName,
        bool verifySha1,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filesToDownload = Init.Download.CheckFilesExists(allFiles, cancellationToken);
        if (filesToDownload.Count == 0)
        {
            progress?.Report((allFiles.Count, allFiles.Count, 0, "所有文件均已存在。"));
        }
        else
        {
            progress?.Report((filesToDownload.Count, 0, 0, $"开始下载{resourceName}文件..."));
            await Init.Download.DownloadListAsync(
                new DownloadProgress(progress, filesToDownload.Count),
                filesToDownload,
                Init.ConfigManager.Data.OlanSettings.MaximumDownloadThreads,
                cancellationToken);
        }

        // 旧下载组件可能在取消后直接返回；必须确认取消状态后再校验或报告完成。
        cancellationToken.ThrowIfCancellationRequested();
        if (verifySha1)
        {
            progress?.Report((allFiles.Count, allFiles.Count, 100, "正在校验文件..."));
            await Init.Download.CheckAllSha1(
                allFiles, Init.ConfigManager.Data.OlanSettings.MaximumSha1Threads, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report((allFiles.Count, allFiles.Count, 100, "处理完成！"));
    }

    // 直接转发给调用方，避免额外的 Progress 调度导致下载通知晚于最终完成通知。
    private sealed class DownloadProgress(
        IProgress<(long AllFiles, long DownedFiles, double Percentage, string DowingFileName)>? progress,
        int count) : IProgress<(int completedFiles, string FilesName)>
    {
        public void Report((int completedFiles, string FilesName) value)
            => progress?.Report((count, value.completedFiles, (double)value.completedFiles / count * 100, value.FilesName));
    }
}
