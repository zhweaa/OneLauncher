using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;

// Independent fixture runner; does not initialize UnitedCore or touch user game data.
try
{
var research = Path.GetFullPath(args.FirstOrDefault() ?? "build/forge-research-20261022");
var output = Path.GetFullPath(args.Skip(1).FirstOrDefault() ?? "build/fg-tests");
Directory.CreateDirectory(output);
var assembly = typeof(Tools).Assembly;
var prefix = "OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.";
var parser = assembly.GetType(prefix + "Installation.ForgeInstallerParser", true)!;
var cases = new[] { ("1.7.10", 0), ("1.8.9", 0), ("1.12.2", 0), ("1.16.5", 4), ("1.19.2", 6), ("1.20.1", 6) };
var assertions = 0;
void Assert(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
object? Call(Type type, object? instance, string method, params object?[] parameters)
{
    try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!.Invoke(instance, parameters); }
    catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
}
T Get<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
void Fail(Action action, string contains)
{
    try { action(); } catch (OlanException ex) { Assert(ex.Message.Contains(contains), ex.Message); return; }
    throw new Exception("Expected failure: " + contains);
}
byte[] Package(string mc) => File.ReadAllBytes(Directory.GetFiles(Path.Combine(research, "inputs"), $"forge-{mc}-*-installer.jar").Single());
object Parse(byte[] bytes, string mc) => Call(parser, null, "Parse", bytes, "Forge", mc, Path.Combine(output, "parse", "libraries"))!;
foreach (var (mc, count) in cases)
{
    var prepared = Parse(Package(mc), mc);
    Assert(Get<List<ForgeSeriesProcessor>>(prepared, "Processors").Count == count, "Processor selection: " + mc);
    Assert(Get<List<NdDowItem>>(prepared, "Downloads").All(d => Uri.IsWellFormedUriString(d.url, UriKind.Absolute)), "Empty URL: " + mc);
    if (mc == "1.12.2") Assert(Get<List<NdDowItem>>(prepared, "Downloads").All(d => !d.path.Contains("mcp_config")), "Unused MCP declaration");
    if (mc == "1.8.9") Assert(Get<List<NdDowItem>>(prepared, "Downloads").All(d => !d.path.Contains("jline")), "Server-only library");
}
Fail(() => Parse(Package("1.20.1"), "1.19.2"), "请求 Minecraft");
Fail(() => Parse(File.ReadAllBytes(Path.Combine(research, "inputs", "forge-1.1-1.3.4.29-client.zip")), "1.1"), "ModLoader");
Assert(Tools.MainClassToModEnum("net.minecraft.launchwrapper.Launch") == ModEnum.none, "Vanilla 1.1 LaunchWrapper");
Assert(Tools.MainClassToModEnum("net.minecraft.launchwrapper.Launch", gameArguments: ["--tweakClass", "cpw.mods.fml.common.launcher.FMLTweaker"]) == ModEnum.forge, "Legacy Forge");
Assert(ModLoaderDetector.Detect("cpw.mods.bootstraplauncher.BootstrapLauncher", ["net.minecraftforge:fmlloader:1.20.1-47.4.0"]) == ModEnum.forge, "Modern Forge");
Assert(ModLoaderDetector.Detect("cpw.mods.bootstraplauncher.BootstrapLauncher", ["net.neoforged.fancymodloader:loader:47.2.2", "net.minecraftforge:eventbus:6.0.5"]) == ModEnum.neoforge, "Early NeoForge");
Fail(() => Tools.MainClassToModEnum("cpw.mods.bootstraplauncher.BootstrapLauncher"), "不能区分");
Fail(() => ModLoaderDetector.Detect("net.fabricmc.loader.impl.launch.knot.KnotClient", ["net.minecraftforge:forge:1.12.2-x"]), "多个加载器");
foreach (var fixture in Directory.GetFiles(Path.Combine(research, "experiment-v2", "metadata"), "neo-*.jar.version.json"))
{
    using var doc = JsonDocument.Parse(File.ReadAllText(fixture));
    var root = doc.RootElement;
    var libs = root.GetProperty("libraries").EnumerateArray().Select(l => l.GetProperty("name").GetString()!).ToList();
    var game = root.GetProperty("arguments").GetProperty("game").EnumerateArray().Select(a => a.GetString()!).ToList();
    Assert(ModLoaderDetector.Detect(root.GetProperty("mainClass").GetString(), libs, game) == ModEnum.neoforge, "Neo fixture");
    var mc = game[game.IndexOf("--fml.mcVersion") + 1];
    var installer = fixture[..^".version.json".Length];
    var neoPrepared = Call(parser, null, "Parse", File.ReadAllBytes(installer), "NeoForge", mc, Path.Combine(output, "neo", "libraries"))!;
    Assert(Get<List<ForgeSeriesProcessor>>(neoPrepared, "Processors").Count > 0, "Neo profile selection");
}
var launch = assembly.GetType(prefix + "Launch.ForgeLaunchArguments", true)!;
Assert(((IEnumerable<string>)Call(launch, null, "LegacyGame", "--username ${auth_player_name} --tweakClass cpw.mods.fml.common.launcher.FMLTweaker")!).SequenceEqual(["--tweakClass", "cpw.mods.fml.common.launcher.FMLTweaker"]), "Legacy incremental args");
var paths = assembly.GetType(prefix + "Installation.ForgePaths", true)!;
Fail(() => Call(paths, null, "Under", output, "../outside.jar"), "超出");
Fail(() => Call(paths, null, "RelativeMaven", "net.forge:a:../x"), "坐标");
Assert(((string)Call(paths, null, "RelativeMaven", "net.minecraftforge:eventbus:0.10.3-milestone.0.1+1a5fa31-service")!).Contains("+1a5fa31"), "Maven build metadata");

using (var client = new HttpClient(new FixtureHandler(research)))
{
    var getter = new ForgeVersionListGetter(client);
    Assert((await getter.GetInstallerUrlAsync(true, "1.7.10")).Contains("1.7.10-10.13.4.1614-1.7.10/"), "Promotions must preserve legacy suffix");
    Assert(ForgeVersionListGetter.GetArtifactUrl(false, "1.20.1", "1.20.1-47.1.106").Contains("net/neoforged/forge/"), "Early Neo artifact");
    Assert((await getter.GetInstallerUrlAsync(false, "1.20.1")).Contains("1.20.1-47.1.106/"), "Neo metadata aliases");
}

await RegressionChecks.RunAsync(output, Assert);

// Actual installs run against official fixture inputs; HTTP is restricted to local saved files.
foreach (var (mc, _) in cases)
{
    var root = Path.Combine(output, mc + "-" + Guid.NewGuid().ToString("N")[..8], "含 空格");
    Directory.CreateDirectory(Path.Combine(root, "versions", mc));
    var original = Path.Combine(root, "versions", mc, mc + ".jar");
    var vanilla = Path.Combine(research, "experiment-v2", "cases", mc, "versions", mc, mc + ".jar");
    File.Copy(vanilla, original);
    File.Copy(Path.ChangeExtension(vanilla, ".json"), Path.Combine(root, "versions", mc, "version.json"));
    var before = SHA1.HashData(File.ReadAllBytes(original));
    using var client = new HttpClient(new FixtureHandler(research));
    var downloadType = assembly.GetType("OneLauncher.Core.DefaultComponent.Downloader.Download", true)!;
    using var download = (IDisposable)Activator.CreateInstance(downloadType, client)!;
    var taskerType = assembly.GetType(prefix + "ForgeSeriesInstallTasker", true)!;
    var tasker = Activator.CreateInstance(taskerType, download, Path.Combine(root, "libraries"), root)!;
    var progress = new List<string>();
    var fileName = Path.GetFileName(Directory.GetFiles(Path.Combine(research, "inputs"), $"forge-{mc}-*-installer.jar").Single());
    var deps = await (Task<List<NdDowItem>>)Call(taskerType, tasker, "StartReadyAsync", "https://fixtures.invalid/" + fileName, "Forge", mc, (Action<string>)progress.Add)!;
    var published = Path.Combine(root, "versions", mc, "version.forge.json");
    Assert(!File.Exists(published) && progress.Count > 0, "Preparation must not publish");
    var java = mc is "1.19.2" or "1.20.1" ? "C:/Program Files/Eclipse Adoptium/jdk-17.0.15.6-hotspot/bin/java.exe" : "C:/Program Files/Eclipse Adoptium/jdk-8.0.452.9-hotspot/bin/java.exe";
    await (Task)Call(taskerType, tasker, "RunProcessorsAsync", original, java, CancellationToken.None)!;
    Assert(File.Exists(published), "Version not published: " + mc);
    Assert(before.SequenceEqual(SHA1.HashData(File.ReadAllBytes(original))), "Vanilla changed");
    Assert(!Directory.Exists(Path.Combine(root, ".forge-install")), "Game root must not contain staging files");
    var verification = JsonNode.Parse(File.ReadAllText(Path.Combine(research, "experiment-v2", "output-verification.json")))!.AsArray()
        .Single(v => v!["Version"]!.GetValue<string>() == mc)!;
    foreach (var check in verification["HashChecks"]!.AsArray())
    {
        var sourcePath = check!["Path"]!.GetValue<string>();
        var relative = sourcePath[(sourcePath.IndexOf("libraries", StringComparison.Ordinal) + "libraries".Length + 1)..];
        var produced = Path.Combine(root, "libraries", relative.Replace('\\', Path.DirectorySeparatorChar));
        Assert(Convert.ToHexStringLower(SHA1.HashData(File.ReadAllBytes(produced))) == check["Expected"]!.GetValue<string>(), "Output checksum: " + mc);
    }
    var strategyType = assembly.GetType("OneLauncher.Core.DefaultComponent.Launcher.Strategys.ForgeStrategy", true)!;
    var strategyTask = (Task)Call(strategyType, null, "CreateAsync", Path.GetDirectoryName(published), root, mc)!;
    await strategyTask;
    var strategy = strategyTask.GetType().GetProperty("Result")!.GetValue(strategyTask)!;
    var gameArgs = ((IEnumerable<string>)Call(strategyType, strategy, "GetAdditionalGameArgs")!).ToList();
    Assert(mc is "1.7.10" or "1.8.9" or "1.12.2" ? gameArgs.Contains("--tweakClass") : gameArgs.Contains("--launchTarget"), "Strategy game args");
    var jvm = ((IEnumerable<string>)Call(strategyType, strategy, "GetAdditionalJvmArgs")!).ToList();
    Assert(jvm.All(a => !a.Contains("${")), "Unresolved JVM token");
    var libraries = (IDictionary<string, string>)Call(strategyType, strategy, "GetModLibraries")!;
    Assert(libraries.Count > 0 && libraries.Values.All(File.Exists), "Strategy runtime libraries");
    File.WriteAllText(Path.Combine(output, $"{mc}.result.json"), JsonSerializer.Serialize(new { mc, deps = deps.Count, gameArgs, jvm, root }));
    Console.WriteLine("PASS installation + strategy: " + mc);
}
Console.WriteLine($"PASS {assertions} assertions; offline fixture HTTP and real Java processors.");
return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}

sealed class FixtureHandler(string research) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!;
        var name = Path.GetFileName(uri.AbsolutePath);
        string? file = null;
        if (name.EndsWith("-installer.jar")) file = Path.Combine(research, "inputs", name);
        else if (name == "promotions_slim.json") file = Path.Combine(research, "inputs", "promotions.json");
        else if (name == "maven-metadata.xml") file = uri.AbsolutePath.StartsWith("/releases/net/neoforged/forge/", StringComparison.Ordinal)
            ? Path.Combine(research, "experiment-v2", "metadata", "neoforge-legacy-metadata.xml")
            : Path.Combine(research, "..", "forge-maven-metadata-20261022.xml");
        else
        {
            var relative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
            var extra = Path.Combine(research, "experiment-v2", "extra-libraries", relative);
            if (File.Exists(extra)) file = extra;
            foreach (var root in Directory.GetDirectories(Path.Combine(research, "experiment-v2", "cases")))
            {
                var candidate = Path.Combine(root, "libraries", relative);
                if (File.Exists(candidate)) { file = candidate; break; }
            }
            // Official mappings URLs are content-addressed; find their saved mapping output by metadata SHA-1.
            if (file == null && name.EndsWith(".txt"))
            {
                foreach (var candidate in Directory.GetFiles(Path.Combine(research, "experiment-v2", "cases"), "*-mappings.txt", SearchOption.AllDirectories))
                    if (uri.AbsolutePath.Contains(Convert.ToHexStringLower(SHA1.HashData(File.ReadAllBytes(candidate))))) { file = candidate; break; }
            }
        }
        if (file == null || !File.Exists(file)) throw new IOException("Missing offline fixture: " + uri);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) });
    }
}
