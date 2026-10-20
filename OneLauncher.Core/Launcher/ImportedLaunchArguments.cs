using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using OneLauncher.Core.Minecraft.JsonModels;
using System.Runtime.InteropServices;
using System.Text;

namespace OneLauncher.Core.Launcher;

/// <summary>从完整导入清单生成参数，不再叠加另一份自动选择的 loader 配置。</summary>
internal static class ImportedLaunchArguments
{
    internal static List<string> Build(VersionInfomations version, string gamePath, UserModel user, string launcherVersion,
        ServerInfo? server = null)
    {
        string root = version.basePath;
        string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        string natives = Path.Combine(gamePath, "natives");
        string classpath = string.Join(Path.PathSeparator, version.GetLibraryiesForUsing().Values.Append(version.GetMainFile().path));
        var replacements = new Dictionary<string, string>
        {
            ["auth_player_name"] = user.Name, ["auth_uuid"] = user.uuid.ToString("N"),
            ["auth_access_token"] = user.AccessToken, ["auth_session"] = user.AccessToken,
            ["user_type"] = user.UserType == AccountType.Msa ? "msa" : user.UserType == AccountType.Yggdrasil ? "mojang" : "legacy", ["user_properties"] = "{}",
            ["version_name"] = version.info.ID!, ["version_type"] = "OneLauncher",
            ["game_directory"] = gamePath, ["assets_root"] = Path.Combine(root, "assets"),
            ["assets_index_name"] = version.GetAssetIndexVersion(),
            ["game_assets"] = Path.Combine(root, "assets", "virtual", version.GetAssetIndexVersion()),
            ["natives_directory"] = natives, ["library_directory"] = Path.Combine(root, "libraries"),
            ["classpath_separator"] = Path.PathSeparator.ToString(), ["classpath"] = classpath,
            ["launcher_name"] = "OneLauncher", ["launcher_version"] = launcherVersion,
            ["clientid"] = Init.ApplicationID, ["auth_xuid"] = "", ["resolution_width"] = "854", ["resolution_height"] = "480"
        };
        var result = new List<string>();
        if (version.info.Arguments?.Jvm is { Count: > 0 } jvm)
            result.AddRange(Expand(jvm, replacements, os, arch));
        else
        {
            if (os == "osx") result.Add("-XstartOnFirstThread");
            result.Add($"-Djava.library.path={natives}");
            result.Add("-cp");
            result.Add(classpath);
        }
        if (version.GetLoggingConfigPath() is { } logging)
            result.Add($"-Dlog4j.configurationFile={logging}");
        result.Add(version.GetMainClass());
        if (version.info.Arguments?.Game is { Count: > 0 } game)
            result.AddRange(Expand(game, replacements, os, arch));
        else if (!string.IsNullOrWhiteSpace(version.info.LegacyGameArguments))
            result.AddRange(Tokenize(version.info.LegacyGameArguments).Select(value => Replace(value, replacements)));
        else
            throw new OlanException("启动失败", "导入实例缺少游戏启动参数，请重新导入完整的版本配置。");
        if (server is { } connection)
        {
            result.AddRange(["--server", connection.Ip, "--port", connection.Port.ToString()]);
            if (Version.TryParse(version.info.ID, out var release) && release >= new Version(1, 20))
                result.AddRange(["--quickPlayMultiplayer", $"{connection.Ip}:{connection.Port}"]);
        }
        return result;
    }

    private static IEnumerable<string> Expand(List<object> arguments, Dictionary<string, string> replacements, string os, string arch)
    {
        foreach (var item in arguments)
        {
            if (item is string text) yield return Replace(text, replacements);
            else if (item is MinecraftArgument argument && Allowed(argument.Rules, os, arch))
            {
                if (argument.Value is string value) yield return Replace(value, replacements);
                else if (argument.Value is List<string> values)
                    foreach (var token in values) yield return Replace(token, replacements);
            }
        }
    }

    private static bool Allowed(List<MinecraftRule>? rules, string os, string arch)
    {
        if (rules == null || rules.Count == 0) return true;
        bool allowed = false;
        foreach (var rule in rules)
        {
            if (rule.Os?.Name != null && rule.Os.Name != os || rule.Os?.Arch != null && rule.Os.Arch != arch) continue;
            if (rule.Features?.Any(feature => feature.Value != (feature.Key == "has_custom_resolution")) == true) continue;
            allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private static string Replace(string text, Dictionary<string, string> replacements)
    {
        string result = Tools.ReplacePlaceholders(text, replacements);
        if (result.Contains("${", StringComparison.Ordinal))
            throw new OlanException("启动失败", "导入实例含有未支持的启动参数占位符。");
        return result;
    }

    // Arguments 接口仍为字符串，因此在边界统一编码单个 argv，而非在替换路径时混入引号。
    internal static string Quote(string token)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in token)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    internal static List<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        foreach (char character in value)
        {
            if (character == quote) { quote = '\0'; continue; }
            if (quote == '\0' && character is '\'' or '"') { quote = character; continue; }
            if (quote == '\0' && char.IsWhiteSpace(character))
            {
                if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); }
            }
            else token.Append(character);
        }
        if (quote != '\0') throw new OlanException("启动失败", "启动参数中存在未闭合的引号。");
        if (token.Length > 0) tokens.Add(token.ToString());
        return tokens;
    }
}
