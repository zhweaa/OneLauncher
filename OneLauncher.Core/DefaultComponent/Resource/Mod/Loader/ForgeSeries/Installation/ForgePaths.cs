using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

internal static class ForgePaths
{
    public static string RelativeMaven(string coordinate)
    {
        if (string.IsNullOrWhiteSpace(coordinate))
            throw new OlanException("Forge 配置无效", "Maven 坐标不能为空。");
        var value = coordinate.StartsWith('[') && coordinate.EndsWith(']') ? coordinate[1..^1] : coordinate;
        var parts = value.Split('@')[0].Split(':');
        if (parts.Length is < 3 or > 4 || value.Count(c => c == '@') > 1 || value.EndsWith('@') ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+' or ':' or '@')) ||
            parts.Any(p => p.Length == 0 || p is "." or ".."))
            throw new OlanException("Forge 配置无效", $"无效的 Maven 坐标：{coordinate}");
        return Path.GetRelativePath(Path.GetFullPath("."), Tools.MavenToPath(Path.GetFullPath("."), value));
    }

    public static string Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Replace('\\', '/').Split('/').Any(p => p is ".." or "."))
            throw new OlanException("Forge 路径无效", $"文件引用超出允许目录：{relative}");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new OlanException("Forge 路径无效", $"文件引用超出允许目录：{relative}");
        return result;
    }

    public static string EntryName(string name)
    {
        var relative = name.TrimStart('/');
        Under(Path.GetTempPath(), relative);
        return relative.Replace('\\', '/');
    }
}
