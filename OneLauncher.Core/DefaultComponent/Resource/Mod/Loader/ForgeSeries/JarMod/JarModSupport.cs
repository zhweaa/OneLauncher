using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.JarMod;

internal static class JarModSupport
{
    public static void RequireInstaller(string minecraft)
    {
        if (Version.TryParse(minecraft, out var version) && version < new Version(1, 6))
            throw Unsupported();
    }

    public static OlanException Unsupported() => new("Forge 历史版本暂不支持",
        "此版本使用 JarMod。需要兼容的 ModLoader/ModLoaderMP 前置组件及旧版启动适配，不能直接用 Forge ZIP 替换原版游戏。请使用已验证的 Forge installer 版本（采样范围为 1.7.10 及以后）。");
}
