using System.Diagnostics;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

internal static class ForgeIntegrationTests
{
    internal sealed record Case(string Loader, string Minecraft);
    internal sealed record Result(string Loader, string Minecraft, string Install, string Launch,
        int? ExitCode, bool RenderInitialized, string? Build, string? Error, string Log, double Seconds,
        int Attempt, bool StopRequested, bool ModLoaded, bool MenuReady, bool WorldReady, bool WorldSaved, bool WorldReopened, string Instance);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    public static async Task<int> RunAsync(UnitedCore core, string[] args)
    {
        string? Value(string option)
        {
            var index = Array.IndexOf(args, option);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        var output = Path.GetFullPath(Value("--output") ?? "build/forge-expanded-20261022/integration");
        Directory.CreateDirectory(output);
        Console.WriteLine("GAME ROOT " + core.VersionListManager.GameRootPath);
        Console.WriteLine("JAVA " + string.Join(", ", core.JavaManager.GetInstalledVersions()));
        if (args.Contains("--info")) return 0;
        var cases = (Value("--cases") ?? "forge:1.14.3,forge:1.20.4,forge:1.21.1,forge:1.21.4,forge:1.21.9,forge:26.2,forge:1.13.1,neoforge:1.20.1,neoforge:1.21.1,neoforge:26.2")
            .Split(',').Select(value =>
            {
                var parts = value.Split(':');
                if (parts.Length != 2 || parts[0] is not ("forge" or "neoforge") ||
                    !Version.TryParse(parts[1], out _)) throw new ArgumentException("--cases 格式为 forge:1.21.1,neoforge:1.21.1");
                return new Case(parts[0], parts[1]);
            }).ToArray();
        var observeSeconds = int.Parse(Value("--seconds") ?? "45");
        var repeat = int.Parse(Value("--repeat") ?? "1");
        if (observeSeconds < 1 || repeat is < 1 or > 10) throw new ArgumentException("--seconds 必须为正数，--repeat 范围为 1 到 10。");
        if (args.Contains("--launch-only") && args.Contains("--install-only")) throw new ArgumentException("不能同时指定 --launch-only 和 --install-only。");
        var probe = args.Contains("--probe-mod");
        if (probe && (args.Contains("--install-only") || cases.Any(c => c.Minecraft != "1.21.1")))
            throw new ArgumentException("--probe-mod 仅支持 Forge/NeoForge 1.21.1 的启动实验。");
        var runId = Guid.NewGuid().ToString("N")[..8];
        var results = new List<Result>();
        // 使用仅驻留内存的离线测试账户，避免刷新真实账户或在日志中保存认证令牌。
        var user = new UserModel(Guid.NewGuid(), "ForgeTest", Guid.NewGuid());
        core.AccountManager.Data.UserDictionary[user.UserID] = user;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            foreach (var run in cases.SelectMany(test => Enumerable.Range(1, repeat).Select(attempt => (Test: test, Attempt: attempt))))
            {
                var test = run.Test;
                var attempt = run.Attempt;
                cancellation.Token.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                var logPath = Path.Combine(output, test.Loader + "-" + test.Minecraft + "-" + attempt + ".log");
                using var log = new StreamWriter(logPath, false) { AutoFlush = true };
                var sync = new object();
                var loggingComplete = false;
                void Log(string message) { lock (sync) { if (!loggingComplete) log.WriteLine(message); } }
                Console.WriteLine($"START {test.Loader} {test.Minecraft} attempt={attempt}/{repeat}");
                var game = new GameData($"ConsoleTest {test.Loader} {test.Minecraft}", test.Minecraft,
                    test.Loader == "forge" ? ModEnum.forge : ModEnum.neoforge, user.UserID)
                {
                    InstanceId = "forge-test-" + test.Loader + "-" + test.Minecraft + "-" + runId,
                    CustomJvmOptimizationArguments = new JvmArguments { mode = OptimizationMode.None },
                    ExtraJvmArguments = "-Xms256m -Xmx2048m"
                };
                Directory.CreateDirectory(game.InstancePath);
                var install = args.Contains("--launch-only") || attempt > 1 ? "existing" : "failed";
                var launchResult = "not-run";
                int? exitCode = null;
                var renderInitialized = false;
                var stopRequested = false;
                var modLoaded = false;
                var menuReady = false;
                var worldReady = false;
                var worldSaved = false;
                var worldReopened = false;
                var probeFailed = false;
                string? error = null;
                string? build = null;
                try
                {
                    if (!args.Contains("--launch-only") && attempt == 1)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                        timeout.CancelAfter(TimeSpan.FromMinutes(20));
                        var info = await DownloadInfo.Create(test.Minecraft,
                            new ModType { IsForge = test.Loader == "forge", IsNeoForge = test.Loader == "neoforge" },
                            isAllowToUseBetaNeoforge: args.Contains("--allow-beta"),
                            isUseRecommendedToInstallForge: args.Contains("--recommended"), defaultGameData: game);
                        var previous = "";
                        var last = Stopwatch.StartNew();
                        var progress = new InlineProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>(p =>
                        {
                            Log($"{p.Title} {p.DownedFiles}/{p.AllFiles} {p.DowingFileName}");
                            if (p.Title == DownProgress.Meta || p.Title.ToString() != previous || last.Elapsed.TotalSeconds >= 10)
                            {
                                Console.WriteLine($"  {test.Minecraft} {p.Title} {p.DownedFiles}/{p.AllFiles}" +
                                    (p.Title == DownProgress.Meta ? " " + p.DowingFileName : ""));
                                previous = p.Title.ToString(); last.Restart();
                            }
                        });
                        // 等待完整安装和版本登记之后，才启动此版本。
                        await core.VersionInstallerService.InstallNewVersion(info, progress, timeout.Token);
                        install = "passed";
                    }
                    using (var installed = JsonDocument.Parse(await File.ReadAllTextAsync(
                        Path.Combine(core.VersionListManager.GameRootPath, "versions", test.Minecraft, $"version.{test.Loader}.json"))))
                        build = installed.RootElement.GetProperty("id").GetString();
                    if (!args.Contains("--install-only"))
                    {
                        if (probe)
                        {
                            var jar = await ForgeProbeMod.CreateAsync(test.Loader, test.Minecraft, core.VersionListManager.GameRootPath,
                                output, Value("--javac") ?? Path.Combine(Path.GetDirectoryName(core.JavaManager.GetJavaExecutablePath(21))!,
                                    OperatingSystem.IsWindows() ? "javac.exe" : "javac"), cancellation.Token);
                            var mods = Path.Combine(game.InstancePath, "mods");
                            Directory.CreateDirectory(mods);
                            File.Copy(jar, Path.Combine(mods, "olan-probe.jar"), true);
                            await File.WriteAllTextAsync(Path.Combine(game.InstancePath, "options.txt"), "lang:en_us\nrenderDistance:2\nsimulationDistance:2\nmaxFps:30\n", cancellation.Token);
                        }
                        using var session = core.GameLaunchService.CreateLaunchService($"ConsoleTest {test.Loader} {test.Minecraft}");
                        session.GameOutputEvent += line =>
                        {
                            Log(line.TrimEnd());
                            if (line.Contains("[OLAN_PROBE] MOD_LOADED", StringComparison.Ordinal)) modLoaded = true;
                            if (line.Contains("[OLAN_PROBE] MENU_READY", StringComparison.Ordinal)) menuReady = true;
                            if (line.Contains("[OLAN_PROBE] WORLD_READY", StringComparison.Ordinal)) worldReady = true;
                            if (line.Contains("[OLAN_PROBE] WORLD_SAVED", StringComparison.Ordinal)) worldSaved = true;
                            if (line.Contains("[OLAN_PROBE] WORLD_REOPEN_REQUESTED", StringComparison.Ordinal)) worldReopened = true;
                            if (line.Contains("[OLAN_PROBE] PROBE_FAILED", StringComparison.Ordinal)) probeFailed = true;
                            if (line.Contains("Backend library: LWJGL version", StringComparison.Ordinal) ||
                                line.Contains("LWJGL version", StringComparison.Ordinal) ||
                                line.Contains("Created:", StringComparison.Ordinal) && line.Contains("textures/atlas", StringComparison.Ordinal))
                                renderInitialized = true;
                        };
                        session.GameClosedEvent += code => exitCode = code;
                        var play = session.Play(game);
                        try
                        {
                            var finished = await Task.WhenAny(play, Task.Delay(TimeSpan.FromSeconds(observeSeconds), cancellation.Token));
                            if (finished == play)
                            {
                                await play;
                                launchResult = exitCode == 0 && renderInitialized ? "exited-after-render" : "failed";
                                if (probe) launchResult = exitCode == 0 && menuReady && modLoaded && worldReady && worldSaved && !probeFailed &&
                                    (attempt == 1 || worldReopened) ? "probe-world-passed" : "probe-failed";
                            }
                            else
                            {
                                // 结束当前游戏并等待 Play 收尾后，才进入下一次下载。
                                stopRequested = true;
                                await session.Stop();
                                await play;
                                launchResult = probe ? "probe-timeout" : renderInitialized ? "render-smoke-passed" : "no-render-marker";
                            }
                        }
                        finally
                        {
                            await session.Stop();
                            // Play also owns the launch session; finish it before starting any subsequent installation.
                            try { await play; } catch when (cancellation.IsCancellationRequested) { }
                        }
                        cancellation.Token.ThrowIfCancellationRequested();
                        Console.WriteLine($"  LAUNCH {launchResult}, exit={exitCode}");
                    }
                }
                catch (Exception exception) when (!cancellation.IsCancellationRequested)
                {
                    error = exception is OlanException olan ? olan.Title + ": " + olan.Message : exception.ToString();
                    Log("FAIL " + error);
                    Console.WriteLine("  FAIL " + error);
                }
                results.Add(new(test.Loader, test.Minecraft, install, launchResult, exitCode, renderInitialized, build, error, logPath, timer.Elapsed.TotalSeconds,
                    attempt, stopRequested, modLoaded, menuReady, worldReady, worldSaved, worldReopened, game.InstancePath));
                await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }), cancellation.Token);
                // Provider 内部 Progress 可能在 await 完成后送达，迟到报告不能访问已关闭的日志。
                lock (sync) loggingComplete = true;
                Console.WriteLine($"END {test.Loader} {test.Minecraft}: install={install}, launch={launchResult}, {timer.Elapsed.TotalSeconds:F1}s");
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            core.AccountManager.Data.UserDictionary.Remove(user.UserID);
        }
        return results.Any(r => r.Install == "failed" || r.Error != null || r.Launch is "failed" or "no-render-marker" or "probe-failed" or "probe-timeout") ? 1 : 0;
    }
}
