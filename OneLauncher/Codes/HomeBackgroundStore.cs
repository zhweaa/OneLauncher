using Avalonia.Media.Imaging;
using OneLauncher.Core.Global;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OneLauncher.Codes;

/// <summary>
/// 主页背景只由物理目录决定，不写入配置：OneLauncher\customdata\home-background\&lt;guid&gt;\&lt;原文件名&gt;。
/// 目录中最新的图片就是当前背景；目录为空表示未设置背景。
/// 每次更换写入新的 guid 目录再清理旧目录，避免覆盖主页正在使用的图片。
/// </summary>
internal static class HomeBackgroundStore
{
    private const string FolderName = "home-background";
    public static readonly string[] ImagePatterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"];
    private static readonly string[] ImageExtensions = ImagePatterns.Select(p => p[1..]).ToArray();

    /// <summary>背景文件被导入或移除后触发。</summary>
    public static event Action? Changed;

    /// <summary>customdata 目录；测试可替换为工作区内的临时目录。</summary>
    internal static string CustomDataPath { get; set; } = Path.Combine(Init.BasePath, "customdata");

    private static string Root => Path.Combine(CustomDataPath, FolderName);

    /// <summary>返回当前背景图片路径，不存在时返回 null。图片能否解码由加载方判断。</summary>
    public static string? FindCurrent()
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

    /// <summary>复制用户选择的图片作为新背景；用户的原始文件不会被修改或删除。</summary>
    public static async Task<string> ImportAsync(string sourcePath)
    {
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Path.GetFileName(sourcePath));
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(destination);
            EnsureValidImage(target);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
        // 新背景就绪后再清理旧背景；旧文件被占用删不掉也无妨，当前背景总是最新的文件。
        foreach (var old in Directory.EnumerateDirectories(Root).Where(d => !PathEquals(d, directory)))
            TryDeleteDirectory(old);
        Changed?.Invoke();
        return target;
    }

    public static void Clear()
    {
        if (Directory.Exists(Root))
        {
            foreach (var directory in Directory.EnumerateDirectories(Root))
                TryDeleteDirectory(directory);
        }
        Changed?.Invoke();
    }

    private static void EnsureValidImage(string path)
    {
        try
        {
            using var bitmap = new Bitmap(path);
            if (bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0)
                throw new InvalidDataException("图片尺寸无效");
        }
        catch (Exception ex)
        {
            throw new OlanException("无法设置主页背景", $"所选文件不是可用的图片：{ex.Message}", OlanExceptionAction.Warning, ex);
        }
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
