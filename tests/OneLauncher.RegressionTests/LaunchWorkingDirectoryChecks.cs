using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;

internal static class LaunchWorkingDirectoryChecks
{
    public static async Task RunAsync(string output)
    {
        // 执行实际 GameLauncher.BasicLaunch，但主类是隔离探针，不启动 Minecraft。
        var javaRoot = Environment.GetEnvironmentVariable("ONELAUNCHER_TEST_JAVA_HOME") ?? Environment.GetEnvironmentVariable("JAVA_HOME");
        var java = javaRoot == null ? "java" : Path.Combine(javaRoot, "bin", "java.exe");
        var javac = javaRoot == null ? "javac" : Path.Combine(javaRoot, "bin", "javac.exe");
        var root = Path.Combine(output, "launch with spaces", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Init.GameRootPath = Path.Combine(root, "minecraft");
        var config = new DBManager(Path.Combine(root, "config.json"));
        await config.InitializeAsync();
        config.Data.AvailableJavas[21] = java;
        typeof(Init).GetProperty("ConfigManager", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, config);
        var accounts = new AccountManager(Path.Combine(root, "accounts.json"));
        await accounts.InitializeAsync();
        var core = (UnitedCore)RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
        typeof(UnitedCore).GetProperty(nameof(UnitedCore.AccountManager))!.SetValue(core, accounts);
        typeof(UnitedCore).GetProperty(nameof(UnitedCore.JavaManager))!.SetValue(core, new JavaManager());
        typeof(UnitedCore).GetProperty("Core", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, core);
        var sourcePath = Path.Combine(root, "WorkingDirectoryProbe.java");
        await File.WriteAllTextAsync(sourcePath, """
            import java.nio.file.*;
            public class WorkingDirectoryProbe {
                public static void main(String[] args) throws Exception {
                    Path gameDir = null, assetsDir = null;
                    for (int i = 0; i < args.length - 1; i++) {
                        if (args[i].equals("--gameDir")) gameDir = Path.of(args[i + 1]);
                        if (args[i].equals("--assetsDir")) assetsDir = Path.of(args[i + 1]);
                    }
                    if (!Path.of("").toRealPath().equals(gameDir.toRealPath())) throw new Exception("user.dir differs from --gameDir");
                    if (!Files.exists(assetsDir.resolve("shared.marker"))) throw new Exception("shared assets not rooted correctly");
                    String value = Files.readString(Path.of("config/fancymenu/customizablemenus.txt"));
                    Files.writeString(Path.of("probe.result"), value);
                    System.out.println("PROBE_OK " + value);
                }
            }
            """);
        var compileInfo = new ProcessStartInfo(javac) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        compileInfo.ArgumentList.Add("--release");
        compileInfo.ArgumentList.Add("21");
        compileInfo.ArgumentList.Add(sourcePath);
        using (var compiler = Process.Start(compileInfo)!)
        {
            var error = await compiler.StandardError.ReadToEndAsync();
            await compiler.WaitForExitAsync();
            if (compiler.ExitCode != 0) throw new Exception(error);
        }
        var versionPath = Path.Combine(Init.GameRootPath, "versions", "1.21.1");
        Directory.CreateDirectory(versionPath);
        using (var zip = ZipFile.Open(Path.Combine(versionPath, "1.21.1.jar"), ZipArchiveMode.Create))
            zip.CreateEntryFromFile(Path.Combine(root, "WorkingDirectoryProbe.class"), "WorkingDirectoryProbe.class");
        await File.WriteAllTextAsync(Path.Combine(versionPath, "version.json"), """
            {"id":"1.21.1","mainClass":"WorkingDirectoryProbe","javaVersion":{"majorVersion":21},"libraries":[],
             "downloads":{"client":{"url":"","size":0}},"assetIndex":{"id":"fixture"},
             "arguments":{"jvm":["-cp","${classpath}"],"game":[]}}
            """);
        Directory.CreateDirectory(Path.Combine(Init.GameRootPath, "assets"));
        await File.WriteAllTextAsync(Path.Combine(Init.GameRootPath, "assets", "shared.marker"), "shared assets");
        var launcherType = typeof(Init).Assembly.GetType("OneLauncher.Core.DefaultComponent.Launcher.GameLauncher", true)!;
        var basicLaunch = launcherType.GetMethod("BasicLaunch", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // BasicLaunch 会写现有诊断文件；测试后恢复原内容。
        var diagnosticPath = Path.Combine(Init.BasePath, "LastLaunchArgs.bat");
        bool hadDirectory = Directory.Exists(Init.BasePath);
        byte[]? previousDiagnostic = File.Exists(diagnosticPath) ? await File.ReadAllBytesAsync(diagnosticPath) : null;
        Directory.CreateDirectory(Init.BasePath);
        try
        {
            foreach (var name in new[] { "instance one", "instance two", "root mode" })
            {
                var instance = new GameData(name, "1.21.1", ModEnum.none, accounts.GetDefaultUser().UserID);
                Directory.CreateDirectory(instance.InstancePath);
                bool rootMode = name == "root mode";
                string gamePath = rootMode ? Init.GameRootPath : instance.InstancePath;
                Directory.CreateDirectory(Path.Combine(gamePath, "config", "fancymenu"));
                await File.WriteAllTextAsync(Path.Combine(gamePath, "config", "fancymenu", "customizablemenus.txt"), name);
                using var launcher = (IDisposable)Activator.CreateInstance(launcherType, nonPublic: true)!;
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                int? exit = null;
                string launchOutput = "";
                launcherType.GetEvent("GameClosedEvent")!.AddEventHandler(launcher, (Action<int>)(code => exit = code));
                launcherType.GetEvent("GameOutputEvent")!.AddEventHandler(launcher, (Action<string>)(text => launchOutput += text));
                await (Task)basicLaunch.Invoke(launcher, [instance, null, rootMode, null, cancellation.Token])!;
                if (exit != 0 || !File.Exists(Path.Combine(gamePath, "probe.result")) ||
                    await File.ReadAllTextAsync(Path.Combine(gamePath, "probe.result")) != name)
                    throw new Exception("Launch working-directory probe failed: " + launchOutput);
            }
        }
        finally
        {
            if (previousDiagnostic != null) await File.WriteAllBytesAsync(diagnosticPath, previousDiagnostic);
            else if (File.Exists(diagnosticPath)) File.Delete(diagnosticPath);
            if (!hadDirectory && !Directory.EnumerateFileSystemEntries(Init.BasePath).Any()) Directory.Delete(Init.BasePath);
        }
        Console.WriteLine("PASS actual GameLauncher Java working directory, instance isolation, root mode, paths with spaces and shared assets");
    }
}
