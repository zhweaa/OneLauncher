using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;

internal static class ForgeProbeMod
{
    public static async Task<string> CreateAsync(string loader, string minecraft, string gameRoot, string output, string compiler, CancellationToken token)
    {
        if (minecraft != "1.21.1") throw new ArgumentException("--probe-mod 当前仅用于 Forge/NeoForge 1.21.1，其他版本需要各自验证适配。");
        var versionPath = Path.Combine(gameRoot, "versions", minecraft, $"version.{loader}.json");
        var model = JsonSerializer.Deserialize(await File.ReadAllTextAsync(versionPath, token), ForgeSeriesJsonContext.Default.ForgeSeriesVersionJson)!;
        var workspace = Path.Combine(output, "probe-mod", loader);
        Directory.CreateDirectory(workspace);
        var source = Path.Combine(workspace, "ProbeMod.java");
        var template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ProbeMod.java"), token);
        await File.WriteAllTextAsync(source, template.Replace("__MOD_PACKAGE__", loader == "forge" ? "net.minecraftforge.fml.common" : "net.neoforged.fml.common"), token);
        var annotation = loader == "forge" ? "net/minecraftforge/fml/common/Mod.class" : "net/neoforged/fml/common/Mod.class";
        string? dependency = null;
        foreach (var library in model.Libraries)
        {
            if (library.Downloads?.Artifact?.Path is not { } relative) continue;
            var path = Path.Combine(gameRoot, "libraries", relative.Replace('/', Path.DirectorySeparatorChar));
            using var archive = ZipFile.OpenRead(path);
            if (archive.GetEntry(annotation) != null) { dependency = path; break; }
        }
        if (dependency == null) throw new IOException("找不到测试模组需要的 @Mod 注解库。");
        // Copy the compile dependency into the experiment folder; javac never modifies installed libraries.
        var localDependency = Path.Combine(workspace, "annotation.jar");
        File.Copy(dependency, localDependency, true);
        var classes = Path.Combine(workspace, "classes");
        Directory.CreateDirectory(classes);
        if (!File.Exists(compiler)) throw new FileNotFoundException("测试模组需要 JDK 编译器，请通过 --javac 指定 javac 的完整路径。", compiler);
        var start = new ProcessStartInfo(compiler) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-proc:none", "--release", "21", "-encoding", "UTF-8", "-cp", localDependency, "-d", classes, source }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动测试模组编译器。");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        var diagnostics = await stdout + await stderr;
        if (process.ExitCode != 0) throw new IOException("测试模组编译失败：" + diagnostics);
        var jar = Path.Combine(workspace, "olan-probe.jar");
        using var stream = new FileStream(jar, FileMode.Create);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(classes, "*.class", SearchOption.AllDirectories))
            zip.CreateEntryFromFile(file, Path.GetRelativePath(classes, file).Replace('\\', '/'));
        var entry = zip.CreateEntry(loader == "forge" ? "META-INF/mods.toml" : "META-INF/neoforge.mods.toml");
        await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        await writer.WriteAsync("modLoader=\"javafml\"\nloaderVersion=\"[1,)\"\nlicense=\"MIT\"\n[[mods]]\nmodId=\"olan_probe\"\nversion=\"1.0\"\ndisplayName=\"OneLauncher Experiment Probe\"\ndescription=\"Menu, test world and restart feasibility experiment.\"\n");
        return jar;
    }
}
