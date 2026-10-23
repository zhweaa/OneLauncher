using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Launch;

namespace OneLauncher.Core.DefaultComponent.Launcher.Strategys;

internal class ForgeStrategy : IModArgStrategy
{
    private readonly ForgeSeriesUsing _parser;
    private readonly string _basePath;
    private readonly string _versionID;
    private ForgeStrategy(string basePath,string versionID)
    {
        _parser = new ForgeSeriesUsing();
        _basePath = basePath;
        _versionID = versionID;
    }
    public static async Task<ForgeStrategy> CreateAsync(string versionPath, string basePath,string versionID)
    {
        var strategy = new ForgeStrategy(basePath,versionID);
        await strategy._parser.Init(basePath, versionID, true);
        return strategy;
    }

    public string GetMainClassOverride() => _parser.info.MainClass;

    public IDictionary<string, string> GetModLibraries()
        => _parser.GetLibrariesForLaunch(_basePath);

    public IEnumerable<string> GetAdditionalJvmArgs()
    {
        var placeholders = new Dictionary<string, string>
        {
            // Forge/NeoForge 的 jvm 参数需要这两个占位符
            { "library_directory", Path.Combine(_basePath, "libraries") },
            { "version_name", _versionID },
#if ONELAUNCHER_TARGET_WINDOWS
            { "classpath_separator" , ";" }
#else
            { "classpath_separator" , ":" }
#endif
        };

        return ForgeLaunchArguments.Expand(_parser.info.Arguments?.Jvm)
            .Select(arg => ForgeLaunchArguments.Encode(Tools.ReplacePlaceholders(arg, placeholders)));
    }

    // Game 参数通常是固定的，但以防万一也加上替换逻辑
    public IEnumerable<string> GetAdditionalGameArgs() => _parser.info.Arguments == null
        ? ForgeLaunchArguments.LegacyGame(_parser.info.MinecraftArguments)
        : ForgeLaunchArguments.Expand(_parser.info.Arguments.Game)
            .Select(ForgeLaunchArguments.Encode);
}
