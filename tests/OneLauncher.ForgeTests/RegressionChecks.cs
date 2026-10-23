using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;

internal static class RegressionChecks
{
    private static readonly Assembly Core = typeof(Tools).Assembly;
    private const string Prefix = "OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.";
    private const string Java17 = "C:/Program Files/Eclipse Adoptium/jdk-17.0.15.6-hotspot/bin/java.exe";
    private const string Coordinate = "net.minecraftforge:forge:1";
    private const string RelativeJar = "net/minecraftforge/forge/1/forge-1.jar";
    private static readonly string OutputHash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes("ok\n")));

    private static Type Type(string name) => Core.GetType(Prefix + name, true)!;
    private static object? Call(Type type, object? instance, string method, params object?[] args)
    {
        try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.Invoke(instance, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }
    private static async Task FailAsync(Func<Task> action, string text, Action<bool, string> assert)
    {
        try { await action(); }
        catch (OlanException ex) { assert(ex.Message.Contains(text), ex.Message); return; }
        throw new Exception("Expected failure: " + text);
    }

    public static async Task RunAsync(string output, Action<bool, string> assert)
    {
        await CheckPclAsync(output, assert);
        assert(ModLoaderDetector.Detect("org.quiltmc.loader.impl.launch.knot.KnotClient") == ModEnum.quilt, "Quilt main class");
        await FailAsync(() => { Tools.MainClassToModEnum("net.minecraft.launchwrapper.Launch", gameArguments: ["--tweakClass", "com.example.OtherTweaker"]); return Task.CompletedTask; }, "未支持", assert);
        var work = Path.Combine(Path.GetTempPath(), "OneLauncherForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var jar = await CompileProbeAsync(work);
            await CheckProcessorAsync(work, jar, assert);
            await CheckInstallFailureAsync(output, jar, assert);
        }
        finally { Directory.Delete(work, true); }
        Console.WriteLine("PASS inheritance, processor outputs, failure, cancellation and retry");
    }

    private static async Task CheckPclAsync(string output, Action<bool, string> assert)
    {
        var versions = Path.Combine(output, "pcl-" + Guid.NewGuid().ToString("N")[..8], "versions");
        async Task<string> Save(string id, object value)
        {
            var path = Path.Combine(versions, id, id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value));
            return path;
        }
        var inspector = Core.GetType("OneLauncher.Core.Helper.ImportPCL2Version.PclVersionInspector", true)!;
        async Task<object> Read(string path)
        {
            var task = (Task)Call(inspector, null, "ReadAsync", path, CancellationToken.None)!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }
        await Save("1.20.1", new { id = "1.20.1", mainClass = "net.minecraft.client.main.Main" });
        var path = await Save("自定义版本", new { id = "Forge?", inheritsFrom = "1.20.1", mainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher",
            libraries = new[] { new { name = "net.neoforged.fancymodloader:loader:47.2.2" }, new { name = "net.minecraftforge:eventbus:6.0.5" } } });
        var result = await Read(path);
        assert((string)result.GetType().GetProperty("Minecraft")!.GetValue(result)! == "1.20.1", "Inherited Minecraft");
        assert((ModEnum)result.GetType().GetProperty("ModLoader")!.GetValue(result)! == ModEnum.neoforge, "Inherited Neo identity");
        path = await Save("legacy", new { id = "legacy", inheritsFrom = "1.20.1", mainClass = "net.minecraft.launchwrapper.Launch", minecraftArguments = "--tweakClass cpw.mods.fml.common.launcher.FMLTweaker" });
        result = await Read(path);
        assert((ModEnum)result.GetType().GetProperty("ModLoader")!.GetValue(result)! == ModEnum.forge, "Legacy PCL args");
        path = await Save("cycle", new { id = "cycle", inheritsFrom = "cycle" });
        await FailAsync(async () => await Read(path), "循环", assert);
        path = await Save("missing", new { id = "missing", inheritsFrom = "absent" });
        await FailAsync(async () => await Read(path), "缺少父版本", assert);
        path = await Save("ambiguous", new { id = "ambiguous", inheritsFrom = "1.20.1", mainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher" });
        await FailAsync(async () => await Read(path), "不能区分", assert);
        path = await Save("custom", new { id = "custom", mainClass = "net.minecraft.client.main.Main" });
        await FailAsync(async () => await Read(path), "基础版本", assert);
        path = await Save("no-main", new { id = "1.20.1" });
        await FailAsync(async () => await Read(path), "缺少启动主类", assert);
    }

    private static async Task<byte[]> CompileProbeAsync(string work)
    {
        var info = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(Java17)!, "javac.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "--release", "8", "-encoding", "UTF-8", "-d", work, Path.Combine(AppContext.BaseDirectory, "Probe.java") }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception("Probe compilation: " + await stderr + await stdout);
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("META-INF/MANIFEST.MF").Open(), Encoding.UTF8))
                writer.Write("Manifest-Version: 1.0\r\nMain-Class: Pr\r\n obe\r\n\r\n");
            using var target = archive.CreateEntry("Probe.class").Open();
            using var source = File.OpenRead(Path.Combine(work, "Probe.class"));
            source.CopyTo(target);
        }
        return memory.ToArray();
    }

    private static byte[] Package(byte[] jar, string mode, int spec = 1)
    {
        var library = new { name = Coordinate, downloads = new { artifact = new { path = RelativeJar, url = "", size = jar.Length, sha1 = Convert.ToHexStringLower(SHA1.HashData(jar)) } } };
        var profile = new { spec, minecraft = "1.20.1", json = "/version.json", data = new Dictionary<string, object>
        {
            ["OUT"] = new { client = "[net.example:output:1@txt]" }, ["OUT_SHA"] = new { client = "'" + OutputHash + "'" },
            ["MARKER"] = new { client = "{ROOT}/marker.txt" }, ["X"] = new { client = "{Y}" }, ["Y"] = new { client = "{X}" }
        }, processors = new[] { new { jar = Coordinate, args = mode == "write" ? new[] { mode, "{OUT}", "{MARKER}" } : new[] { mode },
            outputs = mode == "write" ? new Dictionary<string, string> { ["{OUT}"] = "{OUT_SHA}" } : [] } } };
        var generated = new { name = "net.example:output:1@txt", downloads = new { artifact = new
            { path = "net/example/output/1/output-1.txt", url = "", size = 3, sha1 = OutputHash } } };
        var version = new { id = "fixture", inheritsFrom = "1.20.1", mainClass = "net.minecraftforge.bootstrap.ForgeBootstrap",
            libraries = mode == "write" ? new object[] { library, generated } : [library] };
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Json(string name, object value) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(JsonSerializer.Serialize(value)); }
            Json("install_profile.json", profile);
            Json("version.json", version);
            using var target = archive.CreateEntry("maven/" + RelativeJar).Open();
            target.Write(jar);
        }
        return memory.ToArray();
    }

    private static object Parse(byte[] package, string libraries) => Call(Type("Installation.ForgeInstallerParser"), null, "Parse", package, "Forge", "1.20.1", libraries)!;

    private static async Task CheckProcessorAsync(string work, byte[] jar, Action<bool, string> assert)
    {
        var libraries = Path.Combine(work, "libraries");
        var jarPath = Path.Combine(libraries, RelativeJar);
        Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
        await File.WriteAllBytesAsync(jarPath, jar);
        var package = Package(jar, "write");
        var installer = Path.Combine(work, "installer.jar");
        await File.WriteAllBytesAsync(installer, package);
        var prepared = Parse(package, libraries);
        assert(((List<NdDowItem>)prepared.GetType().GetProperty("Downloads")!.GetValue(prepared)!).Count == 0,
            "Generated runtime library must not require an HTTP download");
        var tokensType = Type("Processors.ForgeProcessorTokens");
        var tokens = Activator.CreateInstance(tokensType, prepared, work, installer, jarPath)!;
        var output = (string)Call(tokensType, tokens, "Key", "OUT")!;
        var marker = (string)Call(tokensType, tokens, "Key", "MARKER")!;
        var profile = (ForgeSeriesInstallProfile)prepared.GetType().GetProperty("Profile")!.GetValue(prepared)!;
        var processor = profile.Processors[0];
        Task Run() => (Task)Call(Type("Processors.ForgeProcessorRunner"), null, "RunAsync", processor, tokens, libraries, Java17, work, (Action<string>)(_ => { }), CancellationToken.None)!;
        await Run();
        await Run();
        assert(File.ReadAllLines(marker).Length == 1, "Verified output must skip processor");
        await File.WriteAllTextAsync(output, "corrupt");
        await Run();
        assert(File.ReadAllLines(marker).Length == 2 && File.ReadAllText(output) == "ok\n", "Corrupt output must be regenerated");
        processor.Outputs.Clear();
        await Run(); await Run();
        assert(File.ReadAllLines(marker).Length == 4, "Absent outputs must not skip processor");
        await FailAsync(() => { Call(tokensType, tokens, "Resolve", "{UNKNOWN}"); return Task.CompletedTask; }, "未定义", assert);
        await FailAsync(() => { Call(tokensType, tokens, "Key", "X"); return Task.CompletedTask; }, "循环", assert);
        await FailAsync(() => { Call(tokensType, tokens, "Resolve", "{ROOT}/../outside"); return Task.CompletedTask; }, "超出", assert);
        processor.Outputs["'" + work + "/../outside.txt'"] = "{OUT_SHA}";
        await FailAsync(Run, "outside.txt", assert);
        await FailAsync(() => { Parse(Package(jar, "write", 99), libraries); return Task.CompletedTask; }, "spec=99", assert);
        var usingModel = new ForgeSeriesUsing { info = new ForgeSeriesVersionJson { Libraries =
            [new() { Name = "net.example:fixture:1:client" }, new() { Name = "net.example:fixture:1:server" }] } };
        foreach (var classifier in new[] { "client", "server" })
        {
            var target = Path.Combine(libraries, "net/example/fixture/1/fixture-1-" + classifier + ".jar");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, jar);
        }
        assert(usingModel.GetLibrariesForLaunch(work).Count == 2, "Classifiers must remain distinct");
    }

    private static async Task CheckInstallFailureAsync(string output, byte[] jar, Action<bool, string> assert)
    {
        var root = Path.Combine(output, "retry-" + Guid.NewGuid().ToString("N")[..8], "含 空格");
        var libraries = Path.Combine(root, "libraries");
        var original = Path.Combine(root, "versions", "1.20.1", "1.20.1.jar");
        var published = Path.Combine(Path.GetDirectoryName(original)!, "version.forge.json");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        await File.WriteAllBytesAsync(original, jar);
        await File.WriteAllTextAsync(published, "previous configuration");
        using var client = new HttpClient(new FixtureHandler(Path.GetFullPath("build/forge-research-20261022")));
        var downloadType = Core.GetType("OneLauncher.Core.DefaultComponent.Downloader.Download", true)!;
        using var download = (IDisposable)Activator.CreateInstance(downloadType, client)!;
        var taskerType = Type("ForgeSeriesInstallTasker");
        var tasker = Activator.CreateInstance(taskerType, download, libraries, root)!;
        var preparedField = taskerType.GetField("_prepared", BindingFlags.NonPublic | BindingFlags.Instance)!;
        void Prepare(string mode) => preparedField.SetValue(tasker, Parse(Package(jar, mode), libraries));
        string? stage = null;
        ProcessorsOut report = (_, _, line) => { if (line.StartsWith("WORK=")) stage = line[5..]; };
        taskerType.GetEvent("ProcessorsOutEvent")!.AddEventHandler(tasker, report);
        Task Run(CancellationToken token) => (Task)Call(taskerType, tasker, "RunProcessorsAsync", original, Java17, token)!;
        Prepare("fail");
        await FailAsync(() => Run(CancellationToken.None), "fixture processor failed", assert);
        assert(stage != null && !Directory.Exists(stage), "Failed installation must clean staging");
        assert(File.ReadAllText(published) == "previous configuration" && !File.Exists(Path.Combine(libraries, RelativeJar)), "Failure must preserve published files");
        Prepare("write");
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15))) await Run(timeout.Token);
        assert(File.ReadAllText(published) != "previous configuration" && File.Exists(Path.Combine(libraries, RelativeJar)), "Retry must release lock and publish");
        var before = File.ReadAllBytes(published);
        Prepare("sleep"); stage = null;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await Run(cancellation.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { assert(cancellation.IsCancellationRequested, "Cancellation token"); }
        assert(stage != null && !Directory.Exists(stage), "Cancelled installation must terminate process and clean staging");
        assert(before.SequenceEqual(File.ReadAllBytes(published)) && jar.SequenceEqual(File.ReadAllBytes(original)), "Cancellation must preserve game files");
    }
}
