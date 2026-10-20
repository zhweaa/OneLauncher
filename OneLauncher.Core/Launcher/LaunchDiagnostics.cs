using System.Text.RegularExpressions;

namespace OneLauncher.Core.Launcher;

public static class LaunchDiagnostics
{
    public static string Redact(string text, string? accessToken = null)
    {
        if (!string.IsNullOrEmpty(accessToken)) text = text.Replace(accessToken, "<redacted>", StringComparison.Ordinal);
        return Regex.Replace(text, "(?i)(--(?:accessToken|auth_session)[\\\"\\s,=:]+)([^\\\"\\s,]+)", "$1<redacted>");
    }

    public static string ReadOptionalLog(string path)
    {
        try { return File.Exists(path) ? Redact(File.ReadAllText(path)) : $"文件不存在：{path}"; }
        catch (IOException exception) { return $"无法读取 {path}：{exception.Message}"; }
        catch (UnauthorizedAccessException exception) { return $"无法读取 {path}：{exception.Message}"; }
    }
}
