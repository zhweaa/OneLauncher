using System.Reflection;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;

internal static class ForgeProtocolChecks
{
    public static async Task<int> RunAsync()
    {
        var parser = typeof(Tools).Assembly.GetType("OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation.ForgeInstallerParser", true)!;
        var output = Path.GetFullPath("build/forge-expanded-20261022/protocol");
        var count = 0;
        foreach (var fixture in Directory.GetFiles("build/forge-expanded-20261022/inputs", "*-installer.jar"))
        {
            using var profile = JsonDocument.Parse(await File.ReadAllTextAsync(fixture + ".install_profile.json"));
            var minecraft = profile.RootElement.GetProperty("minecraft").GetString()!;
            var loader = Path.GetFileName(fixture).StartsWith("neo") ? "NeoForge" : "Forge";
            object prepared;
            try { prepared = parser.GetMethod("Parse")!.Invoke(null, [File.ReadAllBytes(fixture), loader, minecraft, Path.Combine(output, "libraries")])!; }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
            var downloads = (List<NdDowItem>)prepared.GetType().GetProperty("Downloads")!.GetValue(prepared)!;
            if (downloads.Any(d => string.IsNullOrEmpty(d.url))) throw new Exception("Generated library leaked to download plan: " + minecraft);
            Console.WriteLine($"PASS protocol {loader} {minecraft}: {downloads.Count} downloads");
            count++;
        }
        if (!ForgeVersionListGetter.GetArtifactUrl(true, "26.2", "26.2-65.1.5").Contains("26.2-65.1.5")) throw new Exception("Calendar Forge version");
        if (!ForgeVersionListGetter.GetArtifactUrl(false, "26.2", "26.2.0.93").Contains("26.2.0.93")) throw new Exception("Calendar NeoForge version");
        Console.WriteLine($"PASS {count} official installer profiles and calendar version URLs");
        return 0;
    }
}
