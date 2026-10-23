using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Services;
using OneLauncher.Views;
using OneLauncher.Views.Panes;

internal static class DownloadPaneUiChecks
{
    private static Type UiType(string name) => typeof(MainWindow).Assembly.GetType(name, true)!;
    private static object Get(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
    private static void Set(object value, string name, object? data) => value.GetType().GetProperty(name)!.SetValue(value, data);
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!ready())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Download pane fixture timed out");
            await Task.Delay(10);
        }
    }

    public static async Task RunAsync(string output)
    {
        var root = Path.Combine(output, "data", Guid.NewGuid().ToString("N"));
        Init.GameRootPath = Path.Combine(root, "minecraft");
        var config = new DBManager(Path.Combine(root, "config.json"));
        await config.InitializeAsync();
        typeof(Init).GetProperty("ConfigManager", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, config);
        var gamesPath = Path.Combine(root, "games.json");
        var games = new GameDataManager(gamesPath);
        await games.InitializeAsync();
        var accounts = new AccountManager(Path.Combine(root, "accounts.json"));
        await accounts.InitializeAsync();
        var versions = new VersionListManager(Init.GameRootPath);
        await versions.InitializeAsync();
        var version = new VersionBasicInfo("1.21.1", "release", DateTime.UtcNow, "https://fixture.invalid/version");
        var versionPath = Path.Combine(Init.GameRootPath, "versions", version.ID);
        Directory.CreateDirectory(versionPath);
        await File.WriteAllTextAsync(Path.Combine(versionPath, "version.json"), $$$"""
            {"id":"1.21.1","libraries":[],"mainClass":"fixture.Main","javaVersion":{"majorVersion":21},
             "downloads":{"client":{"url":"https://fixture.invalid/client","size":{{{DownloadHandler.Client.Length}}},"sha1":"{{{Convert.ToHexStringLower(SHA1.HashData(DownloadHandler.Client))}}}"}},
             "assetIndex":{"id":"fixture","url":"https://fixture.invalid/assets","size":14}}
            """);
        var indexPath = Path.Combine(Init.GameRootPath, "assets", "indexes");
        Directory.CreateDirectory(indexPath);
        await File.WriteAllTextAsync(Path.Combine(indexPath, "fixture.json"), "{\"objects\":{}}");
        var tasks = new TaskManager();
        var core = (UnitedCore)RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
        Set(core, nameof(UnitedCore.GameDataManager), games);
        Set(core, nameof(UnitedCore.AccountManager), accounts);
        Set(core, nameof(UnitedCore.VersionListManager), versions);
        Set(core, nameof(UnitedCore.TaskManager), tasks);
        Set(core, nameof(UnitedCore.JavaManager), new JavaManager());
        Set(core, nameof(UnitedCore.MojangVersionList), new List<VersionBasicInfo> { version });
        Set(core, nameof(UnitedCore.VersionInstallerService), Activator.CreateInstance(typeof(VersionInstallerService), BindingFlags.NonPublic | BindingFlags.Instance, null, [versions], null));
        typeof(UnitedCore).GetProperty("Core", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, core);
        using var handler = new DownloadHandler();
        using var client = new HttpClient(handler);
        using var download = (IDisposable)Activator.CreateInstance(typeof(Init).Assembly.GetType("OneLauncher.Core.DefaultComponent.Downloader.Download", true)!, client)!;
        typeof(Init).GetProperty("Download", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, download);
        Init.InitTask = Task.FromResult<IServiceCollection>(new ServiceCollection().AddSingleton(core).AddSingleton(config));
        var main = new FixtureMainWindow { Width = 760, Height = 560 };
        WeakReferenceMessenger.Default.UnregisterAll(main);
        var vm = Activator.CreateInstance(UiType("OneLauncher.Views.Panes.PaneViewModels.DownloadPaneViewModel"), version, games, core, (Action)(() => { }))!;
        var pane = (UserControl)Activator.CreateInstance(UiType("OneLauncher.Views.Panes.DownloadPane"))!;
        pane.DataContext = vm;
        main.Content = pane;
        main.Show();
        var start = (IAsyncRelayCommand)Get(vm, "ToDownloadCommand");
        var cancel = (IRelayCommand)Get(vm, "ToCancellationDownloadTaskCommand");
        var center = Activator.CreateInstance(UiType("OneLauncher.Views.Panes.PaneViewModels.TaskCenterPaneViewModel"), core, (Action)(() => { }))!;
        Assert(start.CanExecute(null) && !cancel.CanExecute(null), "initial actions follow task state");
        Capture(main, output, "download-waiting-light.png", ThemeVariant.Light);

        var operation = start.ExecuteAsync(null);
        await handler.Started.Task;
        Assert(tasks.TaskQueue.Count == 1 && !start.CanExecute(null) && cancel.CanExecute(null), "download starts one shared task before awaiting work");
        await Until(() => ((System.Collections.IList)Get(center, "Tasks")).Count == 1);
        var row = ((System.Collections.IList)Get(center, "Tasks"))[0]!;
        Assert(ReferenceEquals(Get(row, "Task"), tasks.TaskQueue.Last()), "task center displays the identical task");
        Capture(main, output, "download-running-dark.png", ThemeVariant.Dark);
        ((IRelayCommand)Get(row, "CancelTaskCommand")).Execute(null);
        Assert((bool)Get(vm, "IsCancelling") && !cancel.CanExecute(null), "task center cancellation immediately disables repeated cancel");
        await operation;
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Canceled && (bool)Get(vm, "IsCanceled") && start.CanExecute(null), "canceled downloads can retry");
        Capture(main, output, "download-canceled.png", ThemeVariant.Light);

        // 下载成功但实例配置保存失败，绝不能先发出 Finished 或显示 100%。
        handler.Hold = false;
        if (File.Exists(gamesPath)) File.Move(gamesPath, gamesPath + ".backup");
        Directory.CreateDirectory(gamesPath);
        var finished = 0;
        tasks.OnTaskCreate += () => tasks.TaskQueue.Last().Finished += () => finished++;
        operation = start.ExecuteAsync(null);
        await Until(() => main.OwnedWindows.Count > 0);
        foreach (var dialog in main.OwnedWindows.ToArray()) dialog.Close();
        await operation;
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Failed && finished == 0 && (double)Get(vm, "CurrentProgress") < 100,
            "instance registration failure never reports download completion");
        Capture(main, output, "download-failed-dark.png", ThemeVariant.Dark);
        Directory.Delete(gamesPath); // 此处只删除本用例创建的空目录。
        if (File.Exists(gamesPath + ".backup")) File.Move(gamesPath + ".backup", gamesPath);
        await start.ExecuteAsync(null);
        await Task.Delay(100);
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Finished && finished == 1 && (bool)Get(vm, "IsFinished") &&
            (double)Get(vm, "CurrentProgress") == 100 && !start.CanExecute(null) && !cancel.CanExecute(null), "registered instance is the only successful terminal state");
        Assert(games.GetDefaultInstance()?.VersionId == version.ID, "successful task registers default instance");
        Capture(main, output, "download-completed-light.png", ThemeVariant.Light);
        Capture(main, output, "download-completed-dark.png", ThemeVariant.Dark);
        main.Width = 650;
        main.Height = 500;
        Capture(main, output, "download-small.png", ThemeVariant.Light);
        var buttons = pane.GetVisualDescendants().OfType<Button>().Where(button => ReferenceEquals(button.Command, start) || ReferenceEquals(button.Command, cancel)).ToArray();
        Assert(buttons.Length == 2 && Math.Abs(buttons[0].Bounds.Width - buttons[1].Bounds.Width) < 1 &&
            buttons[0].Bounds.Width > 250 && ReferenceEquals(buttons[0].Parent, buttons[1].Parent), "footer actions stretch across equal-width columns");
        ((IDisposable)center).Dispose();
        main.Close();
        Console.WriteLine("PASS download pane status icons, task center cancellation, registration failure, retry, terminal guards and screenshots");
    }

    private static void Capture(Window window, string output, string name, ThemeVariant theme)
    {
        Application.Current!.RequestedThemeVariant = theme;
        var until = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(output, name));
    }
    private sealed class FixtureMainWindow : MainWindow
    {
        protected override void OnOpened(EventArgs e) { }
    }
    private sealed class DownloadHandler : HttpMessageHandler
    {
        public static readonly byte[] Client = Encoding.UTF8.GetBytes("download pane client fixture");
        public bool Hold { get; set; } = true;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/client") throw new Exception("Unexpected request " + request.RequestUri);
            Started.TrySetResult();
            if (Hold) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Client) };
        }
    }
}
