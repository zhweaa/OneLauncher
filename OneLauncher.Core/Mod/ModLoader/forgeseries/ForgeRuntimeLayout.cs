using OneLauncher.Core.Global;
using System.IO.Compression;

namespace OneLauncher.Core.Mod.ModLoader.forgeseries;

internal static class ForgeRuntimeLayout
{
    internal static IReadOnlyList<string> RequiredImportedFiles(string minecraft, string? loaderVersion)
    {
        string? forgeVersion = loaderVersion?.StartsWith(minecraft + "-", StringComparison.Ordinal) == true
            ? loaderVersion[(minecraft.Length + 1)..] : loaderVersion;
        // 六项必需规则仅用于本次验证过的 1.20.1 / Forge 47 布局。
        if (minecraft != "1.20.1" || !Version.TryParse(forgeVersion, out var version) || version.Major != 47)
            return [];
        string coordinateVersion = $"{minecraft}-{forgeVersion}";
        string[] components = ["fmlcore", "javafmllanguage", "lowcodelanguage", "mclanguage", "forge:client", "forge:universal"];
        return components.Select(component =>
        {
            string[] parts = component.Split(':');
            string suffix = parts.Length == 2 ? "-" + parts[1] : "";
            return $"net/minecraftforge/{parts[0]}/{coordinateVersion}/{parts[0]}-{coordinateVersion}{suffix}.jar";
        }).ToArray();
    }

    internal static void ValidateImportedFiles(string gameRoot, string minecraft, string? loaderVersion)
    {
        var invalid = new List<string>();
        foreach (string relative in RequiredImportedFiles(minecraft, loaderVersion))
        {
            string path = Path.Combine(gameRoot, "libraries", relative);
            try
            {
                using var archive = ZipFile.OpenRead(path);
                if (archive.Entries.Count == 0) invalid.Add(relative);
            }
            catch (IOException) { invalid.Add(relative); }
            catch (InvalidDataException) { invalid.Add(relative); }
            catch (UnauthorizedAccessException) { invalid.Add(relative); }
        }
        if (invalid.Count > 0)
            throw new OlanException("启动失败", "导入实例的 Forge 运行文件缺失、损坏或无法读取：" +
                Environment.NewLine + string.Join(Environment.NewLine, invalid) + Environment.NewLine +
                "请从原 PCL 实例补全该精确版本的运行文件，或使用修复后的导入器重新导入。");
    }
}
