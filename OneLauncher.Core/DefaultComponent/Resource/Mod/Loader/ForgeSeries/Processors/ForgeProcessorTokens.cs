using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using OneLauncher.Core.Global;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Processors;

/// <summary>客户端 profile 的引用解析。路径值和字面量分开，不静默删除未知参数。</summary>
internal sealed class ForgeProcessorTokens
{
    private readonly ForgePreparedInstall _prepared;
    private readonly string _libraries;
    private readonly string _resources;
    private readonly string _root;
    private readonly bool _extractResources;
    private readonly Dictionary<string, string> _context;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);

    public ForgeProcessorTokens(ForgePreparedInstall prepared, string root, string installer, string minecraftJar)
        : this(prepared, root, installer, minecraftJar, Path.Combine(root, "libraries"), true) { }

    internal ForgeProcessorTokens(ForgePreparedInstall prepared, string root, string installer, string minecraftJar,
        string libraries, bool extractResources)
    {
        _prepared = prepared;
        _root = Path.GetFullPath(root);
        _libraries = Path.GetFullPath(libraries);
        _extractResources = extractResources;
        _resources = Path.Combine(root, "resources");
        _context = new(StringComparer.Ordinal)
        {
            ["SIDE"] = "client", ["ROOT"] = root, ["LIBRARY_DIR"] = _libraries,
            ["INSTALLER"] = installer, ["MINECRAFT_JAR"] = minecraftJar, ["MINECRAFT_VERSION"] = prepared.Minecraft
        };
    }

    public string Key(string name)
    {
        if (_context.TryGetValue(name, out var context)) return context;
        if (_cache.TryGetValue(name, out var cached)) return cached;
        if (_visiting.Count >= 32 || !_visiting.Add(name)) throw new OlanException("Forge 引用无效", $"循环或过深的占位符：{name}");
        try
        {
            if (_prepared.Profile.Data?.Placeholders.TryGetValue(name, out var element) != true)
                throw new OlanException("Forge 引用无效", $"未定义的客户端占位符：{name}");
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (!element.TryGetProperty("client", out element))
                    throw new OlanException("Forge 引用无效", $"占位符没有 client 值：{name}");
            }
            if (element.ValueKind != JsonValueKind.String) throw new OlanException("Forge 引用无效", $"占位符不是字符串：{name}");
            return _cache[name] = Resolve(element.GetString()!);
        }
        finally { _visiting.Remove(name); }
    }

    public string Resolve(string value)
    {
        if (value.Length > 32768) throw new OlanException("Forge 引用无效", "安装参数过长。");
        if (value.StartsWith("'") && value.EndsWith("'")) return value[1..^1];
        if (value.StartsWith('[') && value.EndsWith(']'))
            return ForgePaths.Under(_libraries, ForgePaths.RelativeMaven(value));
        if (value.StartsWith('/'))
        {
            if (!_extractResources) throw new OlanException("Forge 输出配置无效", "处理器输出不能引用安装器内嵌资源。");
            var entryName = ForgePaths.EntryName(value);
            var target = ForgePaths.Under(_resources, entryName);
            if (!File.Exists(target))
            {
                using var stream = new MemoryStream(_prepared.Installer, false);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                var entry = archive.GetEntry(entryName) ?? throw new OlanException("Forge 安装包损坏", $"缺少资源：{value}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
            return target;
        }
        var resolved = Regex.Replace(value, @"\{([A-Za-z_][A-Za-z0-9_]*)\}", m => Key(m.Groups[1].Value),
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (resolved.Contains('{') || resolved.Contains('}')) throw new OlanException("Forge 引用无效", $"无法解析的参数：{value}");
        if (Path.IsPathFullyQualified(resolved))
        {
            var normalized = Path.GetFullPath(resolved);
            if (normalized != _root && !normalized.StartsWith(_root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new OlanException("Forge 引用无效", $"路径超出本次工作目录：{value}");
            return normalized;
        }
        return resolved;
    }

    public bool Has(string key) => _prepared.Profile.Data?.Placeholders.ContainsKey(key) == true;
}
