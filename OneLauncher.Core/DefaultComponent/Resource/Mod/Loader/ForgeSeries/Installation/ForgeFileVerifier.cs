using System.IO.Compression;
using System.Security.Cryptography;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

internal static class ForgeFileVerifier
{
    public static async Task<bool> ValidAsync(string path, IEnumerable<string> hashes, long size, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0 || (size > 0 && new FileInfo(path).Length != size)) return false;
        var expected = hashes.Where(h => !string.IsNullOrWhiteSpace(h)).ToArray();
        if (expected.Length > 0)
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexStringLower(await SHA1.HashDataAsync(stream, token));
            if (!expected.Contains(actual, StringComparer.OrdinalIgnoreCase)) return false;
        }
        if (path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try { using var archive = ZipFile.OpenRead(path); return archive.Entries.Count > 0; }
            catch (InvalidDataException) { return false; }
        }
        return true;
    }

    public static async Task RequireAsync(string path, IEnumerable<string> hashes, long size, CancellationToken token)
    {
        if (!await ValidAsync(path, hashes, size, token))
            throw new OlanException("Forge 文件校验失败", $"文件缺失、长度、摘要或归档结构不正确：{path}");
    }

    public static async Task CopyAtomicAsync(string source, string target, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
