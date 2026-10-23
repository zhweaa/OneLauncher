using OneLauncher.Core.Global;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;

/// <summary>
/// 一个统一的工具类，用于获取 Forge 和 NeoForge 的最新版本和安装器 URL。
/// </summary>
public class ForgeVersionListGetter
{
    private readonly HttpClient _httpClient;

    // API URL 常量
    private const string ForgeMetadataUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string ForgePromotionsUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string NeoForgeMetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

    public ForgeVersionListGetter(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// 根据类型和条件，获取最新的安装器下载 URL。
    /// </summary>
    /// <param name="isForge">为 true 时获取 Forge，为 false 时获取 NeoForge。</param>
    /// <param name="mcVersion">Minecraft 版本号，例如 "1.20.1"。</param>
    /// <param name="allowBeta">【仅用于 NeoForge】是否允许选择 Beta 版本。</param>
    /// <param name="useRecommended">【仅用于 Forge】是否优先使用官方推荐版。</param>
    /// <returns>安装器的完整下载 URL。</returns>
    public async Task<string> GetInstallerUrlAsync(bool isForge, string mcVersion, bool allowBeta = true, bool useRecommended = true)
    {
        if (!Version.TryParse(mcVersion, out var minecraft) || (minecraft.Major != 1 && minecraft.Major < 24))
            throw new OlanException("版本无效", $"不支持的 Minecraft 版本：{mcVersion}");
        if (isForge) JarMod.JarModSupport.RequireInstaller(mcVersion);
        string? fullVersion = isForge
            ? await GetLatestForgeVersionAsync(mcVersion, useRecommended)
            : await GetLatestNeoForgeVersionAsync(mcVersion, allowBeta);

        if (string.IsNullOrEmpty(fullVersion))
        {
            string modType = isForge ? "Forge" : "NeoForge";
            throw new OlanException("版本获取失败", $"未能确定最新的 {modType} 版本。", OlanExceptionAction.Error);
        }

        return GetArtifactUrl(isForge, mcVersion, fullVersion);
    }

    public static string GetArtifactUrl(bool isForge, string mcVersion, string fullVersion)
    {
        if (!Version.TryParse(mcVersion, out var minecraft) || (minecraft.Major != 1 && minecraft.Major < 24))
            throw new OlanException("版本无效", $"不支持的 Minecraft 版本：{mcVersion}");
        // 指定版本也校验坐标，避免将用户输入变成路径或任意下载 URL。
        if (isForge)
        {
            JarMod.JarModSupport.RequireInstaller(mcVersion);
            var version = new ForgeVersionInfo(fullVersion);
            if (version.MinecraftVersion != mcVersion) throw new OlanException("Forge 版本不匹配", fullVersion);
        }
        else if (mcVersion == "1.20.1")
        {
            if (new ForgeVersionInfo(fullVersion).MinecraftVersion != mcVersion)
                throw new OlanException("NeoForge 版本不匹配", fullVersion);
        }
        else if (!fullVersion.StartsWith(ConvertMcVersionToNeoForgePrefix(mcVersion) ?? "invalid", StringComparison.Ordinal))
            throw new OlanException("NeoForge 版本不匹配", fullVersion);
        Installation.ForgePaths.RelativeMaven($"net.example:loader:{fullVersion}");
        var artifact = isForge || mcVersion == "1.20.1" ? "forge" : "neoforge";
        var repository = isForge ? "https://maven.minecraftforge.net/net/minecraftforge" : "https://maven.neoforged.net/releases/net/neoforged";
        return $"{repository}/{artifact}/{fullVersion}/{artifact}-{fullVersion}-installer.jar";
    }

    #region Private Implementation Details

    private async Task<string?> GetLatestForgeVersionAsync(string mcVersion, bool useRecommended)
    {
        var allVersions = await FetchAndParseAllForgeVersionsAsync();
        var candidates = allVersions.Where(v => v.MinecraftVersion == mcVersion).ToList();
        if (candidates.Count == 0)
            throw new OlanException("没有可用的 Forge 构建", $"Forge 官方 Maven 未发布 Minecraft {mcVersion} 对应的构建，请选择官方支持的 Minecraft 版本。");
        try
        {
            var promotions = await FetchForgePromotionsAsync();
            string key = useRecommended ? $"{mcVersion}-recommended" : $"{mcVersion}-latest";
            if (promotions.TryGetValue(key, out var forgeNum) ||
                (useRecommended && promotions.TryGetValue($"{mcVersion}-latest", out forgeNum)))
            {
                var selected = allVersions.FirstOrDefault(v => v.MinecraftVersion == mcVersion && v.ForgeVersion.ToString() == forgeNum);
                if (selected != null) return selected.FullVersionString;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ForgeVersionGetter] 获取 Forge promotions 失败: {ex.Message}。将从完整列表查找。");
        }

        var suitableVersion = candidates.Max();

        return suitableVersion?.FullVersionString;
    }

    private async Task<string?> GetLatestNeoForgeVersionAsync(string mcVersion, bool allowBeta)
    {
        if (mcVersion == "1.20.1")
        {
            var xml = await _httpClient.GetStringAsync("https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml");
            // 仓库同时保留 47.x 别名，只选择带 Minecraft 前缀的完整构件版本。
            return XDocument.Parse(xml).Descendants("version")
                .Where(v => v.Value.StartsWith(mcVersion + "-", StringComparison.Ordinal))
                .Select(v => new ForgeVersionInfo(v.Value)).Max()?.FullVersionString;
        }
        string? prefix = ConvertMcVersionToNeoForgePrefix(mcVersion);
        if (prefix == null) throw new OlanException("无效的 Minecraft 版本", $"无法为 '{mcVersion}' 生成有效的 NeoForge 版本前缀。", OlanExceptionAction.Warning);

        var allVersions = await FetchAndParseAllNeoForgeVersionsAsync();
        var suitableVersion = allVersions.Where(v => v.FullVersionString.StartsWith(prefix) && (allowBeta || !v.IsBeta)).Max();

        return suitableVersion?.FullVersionString;
    }

    private async Task<Dictionary<string, string>> FetchForgePromotionsAsync()
    {
        try
        {
            using var stream = await _httpClient.GetStreamAsync(ForgePromotionsUrl);
            var promoData =
                await JsonSerializer.DeserializeAsync<ForgePromotionData>(
                    stream,ForgeSeriesJsonContext.Default.ForgePromotionData);
            return promoData?.Promos ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            throw new OlanException("获取Forge推荐版本失败", "无法从Forge官方源获取推荐版本列表。", OlanExceptionAction.Warning, ex);
        }
    }

    private async Task<List<ForgeVersionInfo>> FetchAndParseAllForgeVersionsAsync()
    {
        string xmlContent;
        try { xmlContent = await _httpClient.GetStringAsync(ForgeMetadataUrl); }
        catch (HttpRequestException ex) { throw new OlanException("网络错误", "无法从 Forge 官方源获取版本列表。", OlanExceptionAction.Error, ex); }

        try
        {
            return XDocument.Parse(xmlContent).Descendants("version")
                .Select(v => { try { return new ForgeVersionInfo(v.Value); } catch { return null; } })
                .OfType<ForgeVersionInfo>().ToList();
        }
        catch (System.Xml.XmlException ex) { throw new OlanException("元数据格式错误", "Forge 版本列表的 XML 元数据格式无效。", OlanExceptionAction.Error, ex); }
    }

    private static string? ConvertMcVersionToNeoForgePrefix(string mcVersion)
    {
        if (!Version.TryParse(mcVersion, out var parsed) || (parsed.Major != 1 && parsed.Major < 24)) return null;
        var versionPart = (parsed.Major == 1 ? mcVersion[2..] : mcVersion).Split('.');
        if (parsed.Major >= 24) return mcVersion + (versionPart.Length == 2 ? ".0." : ".");
        if (versionPart.Length == 1 && int.TryParse(versionPart[0], out _)) return $"{versionPart[0]}.0.";
        if (versionPart.Length >= 2 && int.TryParse(versionPart[0], out _) && int.TryParse(versionPart[1], out _))
            return $"{versionPart[0]}.{versionPart[1]}.";
        return null;
    }

    private async Task<List<NeoForgeVersionInfo>> FetchAndParseAllNeoForgeVersionsAsync()
    {
        string xmlContent;
        try { xmlContent = await _httpClient.GetStringAsync(NeoForgeMetadataUrl); }
        catch (HttpRequestException ex) { throw new OlanException("网络错误", "无法从 NeoForge 官方源获取版本列表。", OlanExceptionAction.Error, ex); }

        try
        {
            return XDocument.Parse(xmlContent).Descendants("version")
                .Select(v => { try { return new NeoForgeVersionInfo(v.Value); } catch { return null; } })
                .OfType<NeoForgeVersionInfo>().ToList();
        }
        catch (System.Xml.XmlException ex) { throw new OlanException("元数据格式错误", "NeoForge 版本列表的 XML 元数据格式无效。", OlanExceptionAction.Error, ex); }
    }

    #endregion
}
