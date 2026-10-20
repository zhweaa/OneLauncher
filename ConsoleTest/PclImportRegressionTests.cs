using OneLauncher.Core.Downloader;
using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.ImportPCL2Version;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Launcher;
using OneLauncher.Core.Minecraft;
using OneLauncher.Core.Minecraft.JsonModels;
using OneLauncher.Core.Mod.ModLoader.forgeseries;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class PclImportRegressionTests
{
    internal static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), $"OneLauncher-pcl-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await OfflineForgeAsync(root, deadline.Token);
            await MetadataVariantsAsync(root, deadline.Token);
            await MissingGeneratedAsync(root, deadline.Token);
            await CorruptedCacheAsync(root, deadline.Token);
            await DownloadMissingAsync(root, deadline.Token);
            await CancellationAsync(root, deadline.Token);
            await RegistrationFailureAsync(root, deadline.Token);
            await UnsafePathsAsync(root, deadline.Token);
            await InvalidJsonAsync(root, deadline.Token);
            await ExactArgumentsAsync(root, deadline.Token);
            await ImplicitForgeResourcesAsync(root, deadline.Token);
            await MissingImplicitForgeAsync(root, deadline.Token);
            await ImportedPreflightAsync(root, deadline.Token);
            Console.WriteLine("PASS: all 13 PCL import regression tests");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task OfflineForgeAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "offline", token);
        using var handler = new ResourceHandler();
        using var download = new Download(new HttpClient(handler));
        GameData? registered = null;
        var contentProgress = new List<(int Total, int Completed)>();
        var progress = new CallbackProgress(value =>
        {
            if (value.Title == DownProgress.DownAndInstModFiles)
                contentProgress.Add((value.AllFiles, value.DownedFiles));
            if (value.Title == DownProgress.Done)
                Require(registered != null, "导入进度在实例登记前报告完成。");
        });
        var importer = new PCL2Importer(fixture.Target, download, Guid.Empty,
            data => { registered = data; return Task.CompletedTask; }, progress, token);
        await importer.ImportAsync(fixture.Source);
        Require(contentProgress.Count > 1 && contentProgress[0].Completed == 0 &&
            contentProgress[^1].Completed == contentProgress[^1].Total &&
            contentProgress.Select(value => value.Completed).SequenceEqual(Enumerable.Range(0, contentProgress.Count)),
            "Mods、配置和存档迁移没有报告真实文件计数。");
        Require(handler.Requests == 0, "完整本地实例发起了网络请求或重新安装 loader。");
        Require(registered is { ModLoader: ModEnum.forge, ModLoaderVersion: "47.4.20", HasImportedLaunchManifest: true }, "Forge 被误认或版本丢失。");
        string target = Path.Combine(fixture.Target, "instance", registered!.InstanceId);
        Require(File.Exists(Path.Combine(target, "mods", "mod.jar")) && File.Exists(Path.Combine(target, "saves", "world", "level.dat")), "Mods／存档未迁移。");
        Require(!Directory.Exists(Path.Combine(target, "PCL")) && !Directory.Exists(Path.Combine(target, "logs")), "迁移了 PCL 状态或历史日志。");
        var version = new VersionInfomations(await File.ReadAllTextAsync(Path.Combine(target, "launch.json"), token), fixture.Target);
        var args = ImportedLaunchArguments.Build(version, target, OfflineUser(), "test");
        Require(args.Contains("47.4.20") && args.Count(value => value == "--launchTarget") == 1, "启动时加载器配置被替换或叠加。");
        Require(args.Contains(target) && !args.Any(value => value.Contains("${", StringComparison.Ordinal)), "参数路径或占位符解析错误。");
        var builder = await LaunchCommandBuilder.CreateAsync(fixture.Target, "1.20.1", Path.Combine(target, "launch.json"));
        using var command = await builder.SetGamePath(target).SetLoginUser(OfflineUser()).SetModType(ModEnum.forge).BuildCommand();
        string fullArguments = await command.GetArguments();
        Require(fullArguments.Contains("47.4.20", StringComparison.Ordinal) && !fullArguments.Contains("${", StringComparison.Ordinal), "真实 Builder 入口未使用导入清单。");
        var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(registered, GameDataJsonContext.Default.GameData), GameDataJsonContext.Default.GameData);
        Require(roundTrip is { HasImportedLaunchManifest: true, ModLoaderVersion: "47.4.20" }, "实例保存后导入清单标记丢失。");
        Console.WriteLine("PASS: offline Forge import / exact loader version / content / launch arguments / persistence");
    }

    private static async Task MetadataVariantsAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "metadata", token);
        string jsonPath = Path.Combine(fixture.Source, "pack.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath, token))!.AsObject();
        json["libraries"]![0]!["name"] = "net.neoforged:neoforge:21.1.1";
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString(), token);
        Require((await PclVersionResolver.ReadAsync(fixture.Source, token)).ModLoader == ModEnum.neoforge, "NeoForge 识别失败。");
        json["libraries"]![0]!["name"] = "net.fabricmc:fabric-loader:0.16.14";
        json["arguments"]!["game"] = new JsonArray("--username", "${auth_player_name}");
        json["mainClass"] = "net.fabricmc.loader.impl.launch.knot.KnotClient";
        string parent = Path.Combine(root, "metadata-source", "versions", "1.20.1");
        Directory.CreateDirectory(parent);
        json["id"] = "1.20.1";
        await File.WriteAllTextAsync(Path.Combine(parent, "1.20.1.json"), json.ToJsonString(), token);
        var child = new JsonObject { ["id"] = "pack", ["inheritsFrom"] = "1.20.1", ["arguments"] = new JsonObject { ["game"] = new JsonArray("--test", "child") } };
        await File.WriteAllTextAsync(jsonPath, child.ToJsonString(), token);
        var inherited = await PclVersionResolver.ReadAsync(fixture.Source, token);
        Require(inherited.ModLoader == ModEnum.fabric && inherited.LoaderVersion == "0.16.14" && inherited.Manifest["arguments"]!["game"]!.AsArray().Count == 4,
            "父版本或加载器／参数继承丢失。");
        child["inheritsFrom"] = "pack";
        await File.WriteAllTextAsync(jsonPath, child.ToJsonString(), token);
        await ExpectFailureAsync(() => PclVersionResolver.ReadAsync(fixture.Source, token));
        json["libraries"] = new JsonArray(new JsonObject { ["name"] = "net.minecraftforge:forge:1.20.1-47.4.20:client" });
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString(), token);
        var legacy = await PclVersionResolver.ReadAsync(fixture.Source, token);
        Require(legacy.Manifest["libraries"]![0]!["downloads"]!["artifact"]!["path"] != null, "name-only 旧库未加入迁移清单。");
        Console.WriteLine("PASS: Forge/NeoForge/Fabric distinction / parent metadata / inheritance cycle rejection");
    }

    private static async Task ImplicitForgeResourcesAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "implicit-forge", token);
        string jsonPath = Path.Combine(fixture.Source, "pack.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath, token))!.AsObject();
        json["libraries"] = new JsonArray();
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString(), token);
        string otherVersion = Path.Combine(Path.GetDirectoryName(fixture.Library)!, "..", "1.20.1-47.0.0");
        Directory.CreateDirectory(otherVersion);
        await File.WriteAllBytesAsync(Path.Combine(otherVersion, "forge-1.20.1-47.0.0-client.jar"), Jar(), token);
        var metadata = await PclVersionResolver.ReadAsync(fixture.Source, token);
        Require(metadata.RuntimeResources.Count == 6, "未发现六个精确版本的隐式 Forge 资源。");
        using var handler = new ResourceHandler();
        using var download = new Download(new HttpClient(handler));
        GameData? registered = null;
        await new PCL2Importer(fixture.Target, download, Guid.Empty,
            data => { registered = data; return Task.CompletedTask; }, token: token).ImportAsync(fixture.Source);
        foreach (var resource in metadata.RuntimeResources)
        {
            string target = Path.Combine(fixture.Target, "libraries", resource.RelativePath);
            Require(File.Exists(target) && Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(target, token))) == resource.Sha1,
                "隐式资源未迁移或校验失败：" + resource.RelativePath);
        }
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Target, "instance", registered!.InstanceId, "launch.json"), token))!;
        Require(manifest["libraries"]!.AsArray().Count == 0 && handler.Requests == 0,
            "隐式资源污染了 classpath 或发生了非预期联网。");
        Require(!Directory.Exists(Path.Combine(fixture.Target, "libraries/net/minecraftforge/forge/1.20.1-47.0.0")),
            "迁移了无关 Forge 版本。");
        Console.WriteLine("PASS: implicit Forge resources / exact version / unchanged classpath / verified offline migration");
    }

    private static async Task MissingImplicitForgeAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "missing-implicit", token);
        string sourceRoot = Directory.GetParent(fixture.Source)!.Parent!.FullName;
        File.Delete(Path.Combine(sourceRoot, "libraries/net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar"));
        using var handler = new ResourceHandler();
        using var download = new Download(new HttpClient(handler));
        bool registered = false;
        var importer = new PCL2Importer(fixture.Target, download, Guid.Empty,
            _ => { registered = true; return Task.CompletedTask; }, token: token);
        await ExpectFailureAsync(() => importer.ImportAsync(fixture.Source));
        Require(!registered && importer.ImportedInstance == null && handler.Requests == 0 && !Directory.Exists(fixture.Target),
            "缺少隐式核心产物时仍发布实例、联网或留下半成品。");
        Console.WriteLine("PASS: missing implicit Forge core rejected before publication / no partial instance");
    }

    private static async Task ImportedPreflightAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "preflight", token);
        string sourceRoot = Directory.GetParent(fixture.Source)!.Parent!.FullName;
        ForgeRuntimeLayout.ValidateImportedFiles(sourceRoot, "1.20.1", "47.4.20");
        string relative = ForgeRuntimeLayout.RequiredImportedFiles("1.20.1", "47.4.20")[0];
        File.Delete(Path.Combine(sourceRoot, "libraries", relative));
        try
        {
            ForgeRuntimeLayout.ValidateImportedFiles(sourceRoot, "1.20.1", "1.20.1-47.4.20");
            throw new InvalidOperationException("启动前未发现缺失的 Forge 资源。");
        }
        catch (OlanException exception)
        {
            Require(exception.ToString().Contains(relative, StringComparison.Ordinal), "启动失败提示未指出具体文件。");
        }
        Require(ForgeRuntimeLayout.RequiredImportedFiles("1.16.5", "36.2.0").Count == 0 &&
            ForgeRuntimeLayout.RequiredImportedFiles("1.21.1", "52.0.0").Count == 0,
            "将 Forge 47 的布局错误应用到了其他代际。");
        Console.WriteLine("PASS: imported launch preflight / actionable missing-file error / version-specific layout");
    }

    private static async Task MissingGeneratedAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "missing-generated", token);
        File.Delete(fixture.Library);
        using var handler = new ResourceHandler();
        using var download = new Download(new HttpClient(handler));
        bool registered = false;
        var importer = new PCL2Importer(fixture.Target, download, Guid.Empty, _ => { registered = true; return Task.CompletedTask; }, token: token);
        await ExpectFailureAsync(() => importer.ImportAsync(fixture.Source));
        Require(!registered && importer.ImportedInstance == null && handler.Requests == 0, "缺生成文件仍注册或联网重装。");
        Require(!Directory.EnumerateDirectories(Path.Combine(fixture.Target, "instance")).Any(), "失败实例未清理。");
        Console.WriteLine("PASS: missing generated artifact fails / no registration / staging cleanup");
    }

    private static async Task CorruptedCacheAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "corrupt-cache", token);
        string target = Path.Combine(fixture.Target, "libraries", "net", "minecraftforge", "forge", "1.20.1-47.4.20", "forge-client.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, new byte[new FileInfo(fixture.Library).Length], token);
        using var handler = new ResourceHandler();
        using var download = new Download(new HttpClient(handler));
        await new PCL2Importer(fixture.Target, download, Guid.Empty, _ => Task.CompletedTask, token: token).ImportAsync(fixture.Source);
        byte[] targetBytes = await File.ReadAllBytesAsync(target, token);
        byte[] sourceBytes = await File.ReadAllBytesAsync(fixture.Library, token);
        Require(targetBytes.SequenceEqual(sourceBytes) && handler.Requests == 0,
            "同大小坏缓存未通过原实例恢复。");
        Console.WriteLine("PASS: same-size corrupted target repaired from validated source");
    }

    private static async Task DownloadMissingAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "download", token);
        byte[] bytes = await File.ReadAllBytesAsync(fixture.Library, token);
        File.Delete(fixture.Library);
        string jsonPath = Path.Combine(fixture.Source, "pack.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath, token))!.AsObject();
        json["libraries"]![0]!["downloads"]!["artifact"]!["url"] = "https://fixture.invalid/file.jar";
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString(), token);
        using var handler = new ResourceHandler(bytes);
        using var download = new Download(new HttpClient(handler));
        await new PCL2Importer(fixture.Target, download, Guid.Empty, _ => Task.CompletedTask, token: token).ImportAsync(fixture.Source);
        Require(handler.Requests == 1, "缺少可下载资源未补全或发出额外安装器请求。");
        Console.WriteLine("PASS: missing remote artifact downloaded and verified without loader reinstall");
    }

    private static async Task CancellationAsync(string root, CancellationToken token)
    {
        foreach (DownProgress stage in new[] { DownProgress.DownLibs, DownProgress.DownAndInstModFiles })
        {
            var fixture = await FixtureAsync(root, "cancel-" + stage, token);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            bool registered = false;
            using var download = new Download(new HttpClient(new ResourceHandler()));
            var progress = new CallbackProgress(value =>
            {
                if (value.Title == stage && (stage == DownProgress.DownLibs || value.DownedFiles == 1))
                    cancel.Cancel();
            });
            var importer = new PCL2Importer(fixture.Target, download, Guid.Empty, _ => { registered = true; return Task.CompletedTask; }, progress, cancel.Token);
            Task importing = importer.ImportAsync(fixture.Source);
            try { await importing; throw new InvalidOperationException("取消被当成成功。"); }
            catch (OperationCanceledException) { Require(importing.IsCanceled && !registered, "取消状态或注册错误。"); }
            Require(!Directory.EnumerateDirectories(Path.Combine(fixture.Target, "instance")).Any(), "取消留下半成品实例。");
        }
        Console.WriteLine("PASS: cancellation propagates / no registration / cleanup");
    }

    private static async Task RegistrationFailureAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "registration", token);
        using var download = new Download(new HttpClient(new ResourceHandler()));
        var importer = new PCL2Importer(fixture.Target, download, Guid.Empty, _ => throw new IOException("fixture registration failure"), token: token);
        try { await importer.ImportAsync(fixture.Source); throw new InvalidOperationException("注册错误被吞掉。"); }
        catch (IOException) { }
        Require(importer.ImportedInstance == null && !Directory.EnumerateDirectories(Path.Combine(fixture.Target, "instance")).Any(), "注册失败留下可选实例。");
        Console.WriteLine("PASS: registration failure does not report success or retain published instance");
    }

    private static async Task UnsafePathsAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "unsafe", token);
        string jsonPath = Path.Combine(fixture.Source, "pack.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath, token))!.AsObject();
        json["libraries"]![0]!["downloads"]!["artifact"]!["path"] = "../../escaped.jar";
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString(), token);
        using var download = new Download(new HttpClient(new ResourceHandler()));
        await ExpectFailureAsync(() => new PCL2Importer(fixture.Target, download, Guid.Empty, _ => Task.CompletedTask, token: token).ImportAsync(fixture.Source));
        Require(!File.Exists(Path.Combine(root, "escaped.jar")), "资源路径写出目标边界。");
        Console.WriteLine("PASS: unsafe metadata paths rejected before out-of-root write");
    }

    private static async Task InvalidJsonAsync(string root, CancellationToken token)
    {
        var fixture = await FixtureAsync(root, "invalid-json", token);
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, "pack.json"), "not json", token);
        using var download = new Download(new HttpClient(new ResourceHandler()));
        await ExpectFailureAsync(() => new PCL2Importer(fixture.Target, download, Guid.Empty, _ => Task.CompletedTask, token: token).ImportAsync(fixture.Source));
        Require(!Directory.Exists(fixture.Target), "解析失败之前创建了实例或缓存。");
        Console.WriteLine("PASS: invalid version JSON rejected without writes");
    }

    private static async Task ExactArgumentsAsync(string root, CancellationToken token)
    {
        string[] expected = ["路径 含中文", "quoted\"value", "C:\\目录 空格\\", "", "plain"];
        string exe = Environment.ProcessPath!;
        string prefix = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? ImportedLaunchArguments.Quote(typeof(PclImportRegressionTests).Assembly.Location) + " " : "";
        using var process = Process.Start(new ProcessStartInfo(exe)
        {
            Arguments = prefix + "--import-argv-child " + string.Join(" ", expected.Select(ImportedLaunchArguments.Quote)),
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
        })!;
        string output = await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var actual = JsonSerializer.Deserialize(output, MinecraftJsonContext.Default.ListString)!;
        Require(process.ExitCode == 0 && actual.SequenceEqual(expected), "路径／引号／空 argv 被改变。");
        Console.WriteLine("PASS: exact process argv for Unicode / spaces / quotes / trailing slashes / empty value");
    }

    internal static async Task ImportRealAsync(string source, string target)
    {
        // 此模式只向调用者明确指定的新验证目录写入，不使用 Init.Initialize。
        target = Path.GetFullPath(target);
        if (Directory.Exists(target)) throw new InvalidOperationException("真实验证目标必须是尚不存在的独立目录。");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var metadata = await PclVersionResolver.ReadAsync(source, deadline.Token);
        byte[] metadataHash = SHA256.HashData(await File.ReadAllBytesAsync(metadata.VersionJsonPath, deadline.Token));
        using var handler = new RealImportHandler(metadata.Manifest["logging"]?["client"]?["file"]?["url"]?.GetValue<string>());
        using var download = new Download(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) });
        GameData? registered = null;
        long last = 0;
        var progress = new CallbackProgress(value =>
        {
            long now = Environment.TickCount64;
            if (value.Title == DownProgress.Meta || value.Title == DownProgress.Done || now - last > 3000)
            { Console.WriteLine($"IMPORT: {value.Title} {value.DownedFiles}/{value.AllFiles} {value.DowingFileName}"); last = now; }
        });
        var importer = new PCL2Importer(target, download, Guid.Empty, data => { registered = data; return Task.CompletedTask; }, progress, deadline.Token);
        await importer.ImportAsync(source);
        foreach (var resource in metadata.RuntimeResources)
        {
            string file = Path.Combine(target, "libraries", resource.RelativePath);
            Require(File.Exists(file) && Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(file, deadline.Token))) == resource.Sha1,
                "真实隐式 Forge 资源未迁移或内容不一致：" + resource.RelativePath);
        }
        Require(registered != null && handler.Requests <= 1, "真实导入未完成或进行了非预期网络请求。");
        string instance = Path.Combine(target, "instance", registered!.InstanceId);
        var version = new VersionInfomations(await File.ReadAllTextAsync(Path.Combine(instance, "launch.json"), deadline.Token), target);
        var args = ImportedLaunchArguments.Build(version, instance, OfflineUser(), "test");
        int sourceMods = Directory.GetFiles(Path.Combine(source, "mods"), "*.jar").Length;
        int targetMods = Directory.GetFiles(Path.Combine(instance, "mods"), "*.jar").Length;
        Require(sourceMods == targetMods && targetMods > 0, "真实 Mods 数量不一致。");
        byte[] sourceMetadata = await File.ReadAllBytesAsync(metadata.VersionJsonPath, deadline.Token);
        Require(metadataHash.SequenceEqual(SHA256.HashData(sourceMetadata)), "原版本元数据被修改。");
        Require(registered.ModLoader == metadata.ModLoader && registered.ModLoaderVersion == metadata.LoaderVersion, "真实导入 loader 身份发生变化。");
        Require(args.Contains(metadata.LoaderVersion!) && !args.Any(value => value.Contains("${", StringComparison.Ordinal)), "真实启动参数未保留 loader 或存在未解析项。");
        Console.WriteLine($"PASS: real import MC={registered.VersionId}; loader={registered.ModLoader}; version={registered.ModLoaderVersion}; mods={targetMods}; implicitResources={metadata.RuntimeResources.Count}; networkRequests={handler.Requests}");
        Console.WriteLine($"PASS: source metadata unchanged; imported instance={instance}");
        Console.WriteLine("NOTE: only the missing official logging configuration may be downloaded; Minecraft was not launched.");
    }

    private static async Task<Fixture> FixtureAsync(string root, string name, CancellationToken token)
    {
        string sourceRoot = Path.Combine(root, name + "-source");
        string source = Path.Combine(sourceRoot, "versions", "pack");
        Directory.CreateDirectory(source);
        byte[] jar = Jar();
        await File.WriteAllBytesAsync(Path.Combine(source, "pack.jar"), jar, token);
        string libraryPath = "net/minecraftforge/forge/1.20.1-47.4.20/forge-client.jar";
        string library = Path.Combine(sourceRoot, "libraries", libraryPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        await File.WriteAllBytesAsync(library, jar, token);
        foreach (string coordinate in new[] { "fmlcore", "javafmllanguage", "lowcodelanguage", "mclanguage", "forge:client", "forge:universal" })
        {
            string[] parts = coordinate.Split(':');
            string suffix = parts.Length == 2 ? "-" + parts[1] : "";
            string path = Path.Combine(sourceRoot, $"libraries/net/minecraftforge/{parts[0]}/1.20.1-47.4.20/{parts[0]}-1.20.1-47.4.20{suffix}.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, jar, token);
        }
        string indexPath = Path.Combine(sourceRoot, "assets", "indexes", "test.json");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        byte[] index = Encoding.UTF8.GetBytes("{\"objects\":{}}");
        await File.WriteAllBytesAsync(indexPath, index, token);
        var json = new JsonObject
        {
            ["id"] = "pack", ["clientVersion"] = "1.20.1", ["mainClass"] = "cpw.mods.bootstraplauncher.BootstrapLauncher",
            ["javaVersion"] = new JsonObject { ["majorVersion"] = 17 },
            ["downloads"] = new JsonObject { ["client"] = Artifact(jar, "") },
            ["assetIndex"] = Artifact(index, "https://fixture.invalid/index"),
            ["libraries"] = new JsonArray(new JsonObject { ["name"] = "net.minecraftforge:forge:1.20.1-47.4.20:client",
                ["downloads"] = new JsonObject { ["artifact"] = Artifact(jar, "", libraryPath) } }),
            ["arguments"] = new JsonObject
            {
                ["jvm"] = new JsonArray("-cp", "${classpath}", "-DlibraryDirectory=${library_directory}"),
                ["game"] = new JsonArray("--username", "${auth_player_name}", "--gameDir", "${game_directory}", "--launchTarget", "forgeclient", "--fml.forgeVersion", "47.4.20", "--fml.mcVersion", "1.20.1")
            }
        };
        json["assetIndex"]!["id"] = "test";
        await File.WriteAllTextAsync(Path.Combine(source, "pack.json"), json.ToJsonString(), token);
        foreach (string file in new[] { "mods/mod.jar", "saves/world/level.dat", "config/fixture.cfg", "PCL/Setup.ini", "logs/latest.log" })
        {
            string path = Path.Combine(source, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, jar, token);
        }
        return new(source, Path.Combine(root, name + "-target"), library);
    }

    private static JsonObject Artifact(byte[] bytes, string url, string? path = null)
    {
        var value = new JsonObject { ["url"] = url, ["size"] = bytes.Length, ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)) };
        if (path != null) value["path"] = path;
        return value;
    }

    private static byte[] Jar()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("fixture.txt").Open())) writer.Write("fixture");
        return memory.ToArray();
    }

    private static UserModel OfflineUser() => new(Guid.Empty, "Fixture Player", Guid.Empty);
    private static async Task ExpectFailureAsync(Func<Task> action)
    {
        try { await action(); throw new InvalidOperationException("错误输入被当作成功。"); }
        catch (OlanException) { }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record Fixture(string Source, string Target, string Library);
    private sealed class CallbackProgress(Action<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> callback)
        : IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>
    {
        public void Report((DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName) value) => callback(value);
    }
    private sealed class ResourceHandler(byte[]? content = null) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            if (content == null) throw new InvalidOperationException("本地导入不应发起网络请求。");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }
    private sealed class RealImportHandler(string? allowedUrl) : HttpMessageHandler
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.AbsoluteUri != allowedUrl || request.RequestUri.Host != "piston-data.mojang.com")
                throw new InvalidOperationException("真实导入尝试请求非日志配置资源，需检查本地缺项。");
            Interlocked.Increment(ref Requests);
            Console.WriteLine("IMPORT: downloading missing official logging configuration");
            return _http.GetAsync(request.RequestUri, token);
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }
}
