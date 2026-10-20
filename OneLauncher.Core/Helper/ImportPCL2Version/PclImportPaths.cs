using OneLauncher.Core.Global;

namespace OneLauncher.Core.Helper.ImportPCL2Version;

internal static class PclImportPaths
{
    internal static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new OlanException("导入失败", $"配置含有不安全的绝对路径：{relative}");
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        RequireWithin(root, path);
        return path;
    }

    internal static void RequireWithin(string root, string path)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.Equals(fullRoot, Comparison) && !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, Comparison))
            throw new OlanException("导入失败", "配置中的文件路径超出了允许的目录。");
        for (string? current = fullPath; current != null; current = Path.GetDirectoryName(current))
        {
            RequireRegular(current);
            if (current.Equals(fullRoot, Comparison)) break;
        }
    }

    internal static void RequireRegular(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new OlanException("导入失败", $"不能迁移符号链接或目录连接：{Path.GetFileName(path)}");
    }
}
