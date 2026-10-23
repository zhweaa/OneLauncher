using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader;

/// <summary>结合加载器自身构件与参数识别身份。普通 Forge 共用库不能证明加载器是 Forge。</summary>
public static class ModLoaderDetector
{
    public static ModEnum Detect(string? mainClass, IEnumerable<string>? libraries = null, IEnumerable<string>? gameArguments = null)
    {
        var names = (libraries ?? []).Select(n => n.Split('@')[0].Split(':'))
            .Where(p => p.Length >= 2).Select(p => p[0] + ":" + p[1]).ToHashSet(StringComparer.Ordinal);
        var args = (gameArguments ?? []).ToList();
        bool Pair(string key, string value) => Enumerable.Range(0, Math.Max(0, args.Count - 1))
            .Any(i => args[i] == key && args[i + 1] == value);
        var neo = names.Contains("net.neoforged:neoforge") || names.Contains("net.neoforged:forge") ||
            names.Contains("net.neoforged.fancymodloader:loader") || args.Contains("--fml.neoForgeVersion") ||
            Pair("--fml.forgeGroup", "net.neoforged");
        var forge = names.Contains("net.minecraftforge:forge") || names.Contains("net.minecraftforge:fmlloader") ||
            Pair("--tweakClass", "cpw.mods.fml.common.launcher.FMLTweaker") ||
            Pair("--tweakClass", "net.minecraftforge.fml.common.launcher.FMLTweaker") ||
            Pair("--fml.forgeGroup", "net.minecraftforge") || mainClass is "net.minecraftforge.bootstrap.ForgeBootstrap";
        var fabric = names.Contains("net.fabricmc:fabric-loader") || mainClass is
            "net.fabricmc.loader.impl.launch.knot.KnotClient" or "net.fabricmc.loader.launch.knot.KnotClient";
        var quilt = names.Contains("org.quiltmc:quilt-loader") || mainClass == "org.quiltmc.loader.impl.launch.knot.KnotClient";
        // 早期 NeoForge 可以带 Forge 组件；Neo 身份优先于这些共存库。
        var detected = new List<ModEnum>();
        if (neo) detected.Add(ModEnum.neoforge);
        else if (forge) detected.Add(ModEnum.forge);
        if (fabric) detected.Add(ModEnum.fabric);
        if (quilt) detected.Add(ModEnum.quilt);
        if (detected.Count > 1) throw new OlanException("加载器识别冲突", "版本清单中包含多个加载器身份，请检查继承链和依赖。");
        if (detected.Count == 1) return detected[0];
        if (mainClass is "cpw.mods.bootstraplauncher.BootstrapLauncher" or "cpw.mods.modlauncher.Launcher")
            throw new OlanException("无法识别加载器", "共享启动主类不能区分 Forge/NeoForge，需要版本清单中的加载器库或 fml 参数。");
        if (args.Contains("--tweakClass") && !Pair("--tweakClass", "net.minecraft.launchwrapper.VanillaTweaker"))
            throw new OlanException("无法识别加载器", "包含未支持的 tweakClass，无法将该版本识别为原版。");
        if (mainClass is null or "" or "net.minecraft.client.main.Main" or "net.minecraft.launchwrapper.Launch" or "net.minecraft.client.Minecraft")
            return ModEnum.none;
        throw new OlanException("无法识别加载器", $"未识别的启动主类：{mainClass}");
    }
}
