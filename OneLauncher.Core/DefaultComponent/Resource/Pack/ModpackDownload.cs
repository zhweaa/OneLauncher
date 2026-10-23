using System.Diagnostics;

namespace OneLauncher.Core.DefaultComponent.Resource.Pack;

/// <summary>预览与安装共用的已下载整合包；调用方关闭侧栏后释放临时文件。</summary>
public sealed class ModpackDownload : IDisposable
{
    internal string FilePath { get; }
    public MrpackParser Parser { get; }

    internal ModpackDownload(string filePath, MrpackParser parser)
    {
        FilePath = filePath;
        Parser = parser;
    }

    public void Dispose() => DeleteTemporaryFile(FilePath);

    internal static void DeleteTemporaryFile(string filePath)
    {
        try { File.Delete(filePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[ModpackDownload] 清理临时文件失败：{ex.Message}");
        }
    }
}
