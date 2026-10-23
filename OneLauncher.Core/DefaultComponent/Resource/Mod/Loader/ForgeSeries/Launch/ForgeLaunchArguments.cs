using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Launch;

internal static class ForgeLaunchArguments
{
    public static bool Allowed(IEnumerable<JsonElement>? rules)
    {
        if (rules == null) return true;
        var list = rules.ToList();
        if (list.Count == 0) return true;
        var allowed = false;
        foreach (var rule in list)
        {
            var matches = true;
            if (rule.TryGetProperty("os", out var os))
            {
                var name = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
                if (os.TryGetProperty("name", out var n)) matches &= n.GetString() == name;
                if (os.TryGetProperty("arch", out var arch))
                    matches &= Regex.IsMatch(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                        arch.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (os.TryGetProperty("version", out var version))
                    matches &= Regex.IsMatch(Environment.OSVersion.Version.ToString(), version.GetString()!,
                        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            // 下载/启动默认没有 demo 或 quick-play features，不能将未知 feature 当作满足。
            if (rule.TryGetProperty("features", out var features))
                matches &= features.EnumerateObject().All(f => f.Value.ValueKind == JsonValueKind.False);
            if (matches) allowed = rule.GetProperty("action").GetString() == "allow";
        }
        return allowed;
    }

    public static IEnumerable<string> Expand(IEnumerable<JsonElement>? arguments)
    {
        foreach (var arg in arguments ?? [])
        {
            if (arg.ValueKind == JsonValueKind.String) { yield return arg.GetString()!; continue; }
            if (arg.ValueKind != JsonValueKind.Object) throw new OlanException("Forge 参数无效", "参数必须是字符串或规则对象。");
            var rules = arg.TryGetProperty("rules", out var r) ? r.EnumerateArray().ToList() : null;
            if (!Allowed(rules)) continue;
            var value = arg.GetProperty("value");
            if (value.ValueKind == JsonValueKind.String) yield return value.GetString()!;
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) yield return item.GetString()!;
            else throw new OlanException("Forge 参数无效", "规则参数 value 格式不正确。");
        }
    }

    public static IEnumerable<string> LegacyGame(string? arguments)
    {
        var tokens = Tokenize(arguments ?? "");
        // 公共账户和目录参数由 Builder 生成，只追加加载器专用参数。
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "--tweakClass")
            {
                if (++i >= tokens.Count || tokens[i].Contains("${", StringComparison.Ordinal))
                    throw new OlanException("Forge 参数无效", "缺少有效的 tweakClass。");
                yield return "--tweakClass";
                yield return tokens[i];
            }
        }
    }

    public static List<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        var started = false;
        foreach (var c in value)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else token.Append(c);
                started = true;
            }
            else if (c is '\'' or '"') { quote = c; started = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; }
            }
            else { token.Append(c); started = true; }
        }
        if (quote != '\0') throw new OlanException("启动参数无效", "参数引号未闭合。");
        if (started) tokens.Add(token.ToString());
        return tokens;
    }

    // 当前 LaunchCommand 按空格拼接，Strategy 必须编码单个参数，处理器不使用此编码。
    public static string Encode(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')) return value;
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }
}
