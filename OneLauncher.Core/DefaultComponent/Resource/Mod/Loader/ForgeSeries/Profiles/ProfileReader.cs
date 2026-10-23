using System.IO.Compression;
using System.Text.Json;
using OneLauncher.Core.Global;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Profiles;

internal static class ProfileReader
{
    public static (ForgeSeriesVersionJson Version, string Json, string Minecraft) Read(
        ForgeSeriesInstallProfile profile, ZipArchive archive)
    {
        if (profile.Spec is not (0 or 1) || string.IsNullOrEmpty(profile.Minecraft) || string.IsNullOrEmpty(profile.Json))
            throw new OlanException("Forge 安装配置不受支持", $"仅支持 spec 0/1 的客户端 profile（实际 spec={profile.Spec}）。");
        var entry = archive.GetEntry(ForgePaths.EntryName(profile.Json))
            ?? throw new OlanException("Forge 安装包损坏", $"找不到版本配置 {profile.Json}。");
        using var reader = new StreamReader(entry.Open());
        var json = reader.ReadToEnd();
        var version = JsonSerializer.Deserialize(json, ForgeSeriesJsonContext.Default.ForgeSeriesVersionJson)
            ?? throw new OlanException("Forge 安装配置无效", "无法读取版本配置。");
        return (version, json, profile.Minecraft);
    }
}
