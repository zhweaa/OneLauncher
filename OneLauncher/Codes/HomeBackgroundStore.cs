using OneLauncher.Core.Global;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OneLauncher.Codes;

/// <summary>
/// 主页背景图片保存在 OneLauncher\customdata\home-background\&lt;guid&gt;\&lt;原文件名&gt;。
/// 每次更换都写入新目录，避免覆盖主页正在显示的旧图片；用户原始文件永远不会被启动器删除。
/// </summary>
internal static class HomeBackgroundStore
{
    private const string FolderName = "home-background";
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];

    /// <summary>customdata 目录；测试可替换为工作区内的临时目录。</summary>
    internal static string CustomDataPath { get; set; } = Path.Combine(Init.BasePath, "customdata");

    private static string Root => Path.Combine(CustomDataPath, FolderName);

    public static async Task<string> ImportAsync(string sourcePath)
    {
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Path.GetFileName(sourcePath));
        try
        {
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(destination);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
        return target;
    }

    /// <summary>
    /// 返回最近一次复制的背景图片。配置被旧版本启动器重写时会丢失背景字段，
    /// 但复制的文件仍在，切换到魔法按钮时用它恢复背景。
    /// </summary>
    public static string? FindLatest()
    {
        if (!Directory.Exists(Root))
            return null;
        try
        {
            return new DirectoryInfo(Root).EnumerateDirectories()
                .SelectMany(d => d.EnumerateFiles())
                .Where(f => ImageExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static bool IsManaged(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>删除启动器复制的背景；删除失败（例如文件仍被占用）时保留文件，不影响设置保存。</summary>
    public static void TryDelete(string? path)
    {
        if (!IsManaged(path))
            return;
        var fullPath = Path.GetFullPath(path!);
        var directory = Path.GetDirectoryName(fullPath)!;
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            // 只删除 guid 子目录，绝不删除 home-background 根目录。
            if (string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, true);
            else
                File.Delete(fullPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>移除背景时清空全部副本，避免之后切换样式时把已移除的背景重新找回。</summary>
    public static void TryDeleteAll()
    {
        if (!Directory.Exists(Root))
            return;
        foreach (var directory in Directory.EnumerateDirectories(Root))
            TryDeleteDirectory(directory);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
