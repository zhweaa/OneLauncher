using System.Text.Json;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Legacy;

internal static class LegacyProfileReader
{
    public static (ForgeSeriesVersionJson Version, string Json, string Minecraft) Read(
        ForgeSeriesInstallProfile profile, JsonElement root)
    {
        if (profile.Install == null || profile.VersionInfo == null || string.IsNullOrEmpty(profile.Install.Minecraft))
            throw new OlanException("Forge 安装配置无效", "旧安装包缺少 install 或 versionInfo。");
        return (profile.VersionInfo, root.GetProperty("versionInfo").GetRawText(), profile.Install.Minecraft);
    }
}
