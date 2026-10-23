using OneLauncher.Core.DefaultComponent.Mod.ModLoader.fabric;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.Fabric;
using OneLauncher.Core.Global;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Launcher.Strategys;

internal class FabricStrategy : IModArgStrategy
{
    private readonly FabricVJParser _fabricParser;

    private FabricStrategy(FabricVJParser p )
    {
        _fabricParser = p;
    }
    public static async Task<FabricStrategy> CreateAsync(string versionPath, string basePath)
    {
        string fabricJsonPath = Path.Combine(versionPath, "version.fabric.json");
        await using var fs = File.OpenRead(fabricJsonPath);
        return new FabricStrategy(new FabricVJParser(
            await JsonSerializer.DeserializeAsync<FabricRoot>(fs,FabricJsonContext.Default.FabricRoot)
            ?? throw new OlanException("启动失败",$"在解析文件'{fabricJsonPath}'时出错"),basePath));
    }

    public string? GetMainClassOverride() => _fabricParser.GetMainClass();

    // Fabric需要优先加载自己的库
    //public IEnumerable<(string key, string path)> GetModLibraries()=> _fabricParser.GetLibrariesForUsing().Select(x => (string.Join(string.Empty, x.name.Split(':')[..^1]), x.path));
    public IDictionary<string, string> GetModLibraries() => _fabricParser.GetLibrariesForUsing();


    // Fabric本身不添加额外的JVM和游戏参数
    public IEnumerable<string> GetAdditionalJvmArgs() => Enumerable.Empty<string>();
    public IEnumerable<string> GetAdditionalGameArgs() => Enumerable.Empty<string>();
}

