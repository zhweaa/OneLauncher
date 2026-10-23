using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using OneLauncher.Core.DefaultComponent.Resource.ModService.Modrinth;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Services;
using OneLauncher.Views;

internal static class ShaderUiChecks
{
    private static readonly Assembly Ui = typeof(MainWindow).Assembly;
    private static Type Type(string name) => Ui.GetType(name, throwOnError: true)!;
    private static object Get(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
    private static void Set(object value, string name, object? property) => value.GetType().GetProperty(name)!.SetValue(value, property);
    private static object New(string name, params object[] args) => Activator.CreateInstance(Type(name), args)!;
    private static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name)!.Invoke(value, args)!;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("UI fixture timed out");
            await Task.Delay(10);
        }
    }

    public static async Task RunAsync(string output)
    {
        const string browserType = "OneLauncher.Views.ViewModels.ModsBrowserViewModel";
        const string factoryType = "OneLauncher.ViewModel.PaneViewModels.Factories.InstallModPaneViewModelFactory";
        string root = Path.Combine(output, "data", Guid.NewGuid().ToString("N"));
        Init.GameRootPath = Path.Combine(root, "minecraft");
        var config = new DBManager(Path.Combine(root, "config.json"));
        await config.InitializeAsync();
        typeof(Init).GetProperty("ConfigManager", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, config);
        var games = new GameDataManager(Path.Combine(root, "games.json"));
        await games.InitializeAsync();
        var instances = new[] { ModEnum.none, ModEnum.fabric, ModEnum.forge, ModEnum.neoforge, ModEnum.quilt }
            .Select(loader => new GameData(loader + " 实例", "1.21.1", loader, Guid.Empty)).ToArray();
        foreach (var instance in instances) await games.AddGameDataAsync(instance);
        var tasks = new TaskManager();
        var core = (UnitedCore)RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
        Set(core, nameof(UnitedCore.GameDataManager), games);
        Set(core, nameof(UnitedCore.TaskManager), tasks);
        Set(core, nameof(UnitedCore.MojangVersionList), new List<OneLauncher.Core.Helper.Models.VersionBasicInfo>());
        Set(core, nameof(UnitedCore.ResourceInstallerService), Activator.CreateInstance(typeof(ResourceInstallerService), nonPublic: true));
        using var handler = new ResourceHandler();
        using var client = new HttpClient(handler);
        var downloadType = typeof(Init).Assembly.GetType("OneLauncher.Core.DefaultComponent.Downloader.Download", true)!;
        using var download = (IDisposable)Activator.CreateInstance(downloadType, client)!;
        typeof(Init).GetProperty("Download", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, download);

        // 共用搜索入口保留模组筛选，并正确编码光影类型与特殊关键词。
        var search = new SearchModrinth();
        await search.ToSearch("光影 & + ?", ResourceType.ShaderPack);
        var query = Uri.UnescapeDataString(handler.Requests[^1].Query);
        Assert(query.Contains("project_type:shader") && !query.Contains("categories:"), "shader query must ignore mod loaders");
        Assert(handler.Requests[^1].Query.Contains(Uri.EscapeDataString("光影 & + ?")), "keyword URL encoding");
        await search.ToSearch("");
        query = Uri.UnescapeDataString(handler.Requests[^1].Query);
        Assert(query.Contains("project_type:mod") && query.Contains("categories:quilt"), "default search preserves mod facets");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await search.ToSearch("", ResourceType.ShaderPack, canceled.Token); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { }
        }

        var services = new ServiceCollection().AddSingleton(core).AddSingleton(config);
        Init.InitTask = Task.FromResult<IServiceCollection>(services);
        var main = new FixtureMainWindow { Width = 760, Height = 560 };
        // 直接检查通知内容；不依赖主窗口通知视觉树或启动真实页面初始化。
        WeakReferenceMessenger.Default.UnregisterAll(main);
        var factory = main.provider.GetRequiredService(Type(factoryType));
        var browser = New(browserType, factory, main.provider.GetRequiredService(
            Type("OneLauncher.ViewModel.PaneViewModels.Factories.InstallPackPaneViewModelFactory")));
        var page = new ModsBrowser { DataContext = browser };
        main.Content = page;
        main.Show();
        await Until(() => !(bool)Get(browser, "IsSearching"));
        Assert(((System.Collections.IList)Get(browser, "Items")).Count == 1, "initial mod search");
        var selector = page.FindControl<ComboBox>("ResourceTypeSelector")!;
        Assert(selector.Items.Count == 3, "resource dropdown has three choices");
        selector.SelectedIndex = 1;
        await Until(() => !(bool)Get(browser, "IsSearching"));
        var shader = ((System.Collections.IList)Get(browser, "Items"))[0]!;
        Assert((ResourceType)Get(shader, "Type") == ResourceType.ShaderPack, "dropdown updates search resource type");
        Assert(((List<string>)Get(shader, "SupportVersions")).Contains("24w40a"), "shader snapshot versions remain intact");
        Assert((string)Get(browser, "SearchWatermark") == "搜索光影包...", "shader search prompt");
        Capture(main, output, "shader-browser-light.png", ThemeVariant.Light);
        Capture(main, output, "shader-browser-dark.png", ThemeVariant.Dark);
        var installButton = page.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("item-action"));
        Assert(installButton.IsEnabled && ReferenceEquals(installButton.CommandParameter, shader)
            && ReferenceEquals(installButton.Command, Get(browser, "ToInstallResourceCommand")), "resource card install binding");

        Set(browser, "SearchContent", "test");
        Assert((bool)Get(browser, "CanClear"), "clear button appears for nonempty input");
        ((IRelayCommand)Get(browser, "ClearTextBoxCommand")).Execute(null);
        Assert(!(bool)Get(browser, "CanClear"), "clear button disappears after clearing");
        Set(browser, "SearchContent", "slow");
        var oldSearch = (Task)Call(browser, "RefList");
        await handler.SlowStarted.Task;
        Set(browser, "SearchContent", "latest");
        await (Task)Call(browser, "RefList");
        handler.SlowRelease.TrySetResult();
        await oldSearch;
        Assert((string)Get(((System.Collections.IList)Get(browser, "Items"))[0]!, "Title") == "latest", "stale query cannot overwrite latest results");
        Set(browser, "SearchContent", "empty");
        await (Task)Call(browser, "RefList");
        Assert(((System.Collections.IList)Get(browser, "Items")).Count == 0 && ((string)Get(browser, "SearchStatus")).Length > 0, "empty search result is visible");
        Set(browser, "SearchContent", "error");
        var searchFailure = (Task)Call(browser, "RefList");
        await Until(() => main.OwnedWindows.Count > 0);
        foreach (var dialog in main.OwnedWindows.ToArray()) dialog.Close();
        await searchFailure;
        Assert(((string)Get(browser, "SearchStatus")).Contains("搜索失败"), "search failure is visible");
        Set(browser, "SearchContent", "");
        await (Task)Call(browser, "RefList");
        shader = ((System.Collections.IList)Get(browser, "Items"))[0]!;
        await Until(() => page.GetVisualDescendants().OfType<Button>().Any(button =>
            button.Classes.Contains("item-action") && ReferenceEquals(button.CommandParameter, shader)));
        installButton = page.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("item-action"));
        installButton.Command!.Execute(installButton.CommandParameter);
        var pane = (UserControl)Get(browser, "PaneContent");
        var vm = pane.DataContext!;
        Assert(pane.GetType().Name == "InstallModPane" && !(bool)Get(vm, "IsMod"), "shader uses common install pane");
        Assert(!pane.FindControl<CheckBox>("DependenciesCheckBox")!.IsVisible, "shader hides dependency option");
        Assert(!((IAsyncRelayCommand)Get(vm, "ToInstallCommand")).CanExecute(null), "instance required before installing");
        Capture(main, output, "shader-pane-light.png", ThemeVariant.Light);
        Capture(main, output, "shader-pane-dark.png", ThemeVariant.Dark);
        var versionsExpander = pane.FindControl<Expander>("SupportedVersionsExpander")!;
        versionsExpander.IsExpanded = true;
        Capture(main, output, "supported-versions-light.png", ThemeVariant.Light);
        Capture(main, output, "supported-versions-dark.png", ThemeVariant.Dark);
        versionsExpander.IsExpanded = false;

        // 原版及各种加载器实例均可安装，同一 Pane 使用后端实例路径。
        foreach (var instance in instances)
        {
            Set(vm, "SelectedGameData", instance);
            await (Task)Call(vm, "ToInstall");
            Assert(File.Exists(Path.Combine(instance.InstancePath, "shaderpacks", "fixture.zip")), "shader target path for " + instance.ModLoader);
            Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Finished, "shader completed for " + instance.ModLoader);
        }
        Dispatcher.UIThread.RunJobs();
        Assert((double)Get(vm, "CurrentProgress") == 100 && (string)Get(vm, "Dp") == "光影包安装完成", "late progress cannot overwrite terminal display");
        Assert(handler.Requests.Where(uri => uri.AbsolutePath == "/v2/project/shader-project/version").All(uri => !uri.Query.Contains("loaders")), "no shader loader filtering");
        Capture(main, output, "shader-completed.png", ThemeVariant.Light);

        // 保存不存在的选中实例必须拦截，不能创建安装任务。
        var deleted = new GameData("deleted", "1.21.1", ModEnum.none, Guid.Empty);
        Set(vm, "SelectedGameData", deleted);
        int count = tasks.TaskQueue.Count;
        await (Task)Call(vm, "ToInstall");
        Assert(tasks.TaskQueue.Count == count && GetNullable(vm, "SelectedGameData") == null, "deleted instance guard");

        // 用新实例强制下载，验证运行进度、重复点击与任务中心取消。
        var cancelInstance = new GameData("cancel", "1.21.1", ModEnum.none, Guid.Empty);
        await games.AddGameDataAsync(cancelInstance);
        Set(vm, "SelectedGameData", cancelInstance);
        handler.HoldDownloads = true;
        var install = (Task)Call(vm, "ToInstall");
        await handler.DownloadStarted.Task;
        Dispatcher.UIThread.RunJobs();
        Assert((bool)Get(vm, "IsInstalling") && (double)Get(vm, "CurrentProgress") < 100, "running download never appears completed");
        count = tasks.TaskQueue.Count;
        await (Task)Call(vm, "ToInstall");
        Assert(tasks.TaskQueue.Count == count, "duplicate install does not create a second task");
        Capture(main, output, "shader-running.png", ThemeVariant.Light);
        var center = New("OneLauncher.Views.Panes.PaneViewModels.TaskCenterPaneViewModel", core, (Action)(() => { }));
        var taskRow = ((System.Collections.IList)Get(center, "Tasks"))[0]!;
        ((IRelayCommand)Get(taskRow, "CancelTaskCommand")).Execute(null);
        Assert((bool)Get(vm, "IsCancelling") && !((IRelayCommand)Get(vm, "CancelInstallCommand")).CanExecute(null),
            "external cancellation updates common pane and disables repeated cancel");
        await install;
        ((IDisposable)center).Dispose();
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Canceled && !(bool)Get(vm, "IsInstalling"), "task center cancellation preserves canceled state");
        Capture(main, output, "shader-canceled.png", ThemeVariant.Dark);
        handler.HoldDownloads = false;
        await (Task)Call(vm, "ToInstall");
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Finished, "retry after cancellation");

        // 后端兼容版本错误映射为失败，由现有异常界面显示。
        handler.NoVersions = true;
        var failing = (Task)Call(vm, "ToInstall");
        await Until(() => tasks.TaskQueue.Last().Status == TaskInfoStatus.Failed);
        await Until(() => main.OwnedWindows.Count > 0);
        foreach (var dialog in main.OwnedWindows.ToArray()) dialog.Close();
        await failing;
        Assert((TaskInfoStatus)Get(vm, "InstallationStatus") == TaskInfoStatus.Failed, "version failure is displayed");
        Capture(main, output, "shader-failed.png", ThemeVariant.Light);
        handler.NoVersions = false;

        // 旧 Pane 的关闭回调不能关闭已经替换的新 Pane。
        var replacement = new UserControl();
        Set(browser, "PaneContent", replacement);
        Set(browser, "IsPaneShow", true);
        ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);
        Assert((bool)Get(browser, "IsPaneShow"), "old pane close callback must preserve replacement pane");
        Set(browser, "IsPaneShow", false);
        selector.SelectedIndex = 0;
        await Until(() => !(bool)Get(browser, "IsSearching"));
        var mod = ((System.Collections.IList)Get(browser, "Items"))[0]!;
        ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(mod);
        pane = (UserControl)Get(browser, "PaneContent");
        vm = pane.DataContext!;
        Assert((bool)Get(vm, "IsMod") && pane.FindControl<CheckBox>("DependenciesCheckBox")!.IsVisible, "mod retains dependency option");
        Set(vm, "SelectedGameData", instances[0]);
        count = tasks.TaskQueue.Count;
        await (Task)Call(vm, "ToInstall");
        Assert(tasks.TaskQueue.Count == count, "mod still checks loader compatibility");
        Set(vm, "SelectedGameData", instances[1]);
        Set(vm, "IsICS", false);
        await (Task)Call(vm, "ToInstall");
        Assert(File.Exists(Path.Combine(instances[1].InstancePath, "mods", "fixture.jar")), "shared pane still installs mods");
        Capture(main, output, "mod-pane.png", ThemeVariant.Light);

        // 共用 Pane 的模组元数据请求也接受任务取消。
        var slowModInstance = new GameData("slow-mod", "1.21.1", ModEnum.fabric, Guid.Empty);
        await games.AddGameDataAsync(slowModInstance);
        Set(vm, "AvailableGameData", games.AllGameData);
        Set(vm, "SelectedGameData", slowModInstance);
        handler.HoldModMetadata = true;
        var slowMod = (Task)Call(vm, "ToInstall");
        await Until(() => handler.ModStarted.Task.IsCompleted);
        ((IRelayCommand)Get(vm, "CancelInstallCommand")).Execute(null);
        await slowMod;
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Canceled, "mod metadata cancellation");
        handler.HoldModMetadata = false;

        // 返回关闭安装层时同时取消当前下载。
        selector.SelectedIndex = 1;
        await Until(() => !(bool)Get(browser, "IsSearching"));
        shader = ((System.Collections.IList)Get(browser, "Items"))[0]!;
        ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(shader);
        vm = ((UserControl)Get(browser, "PaneContent")).DataContext!;
        var closeInstance = new GameData("close-cancel", "1.21.1", ModEnum.none, Guid.Empty);
        await games.AddGameDataAsync(closeInstance);
        Set(vm, "AvailableGameData", games.AllGameData);
        Set(vm, "SelectedGameData", closeInstance);
        handler.HoldDownloads = true;
        var closing = (Task)Call(vm, "ToInstall");
        await Until(() => handler.Requests[^1].Host == "files.invalid");
        ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);
        await closing;
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Canceled && !(bool)Get(browser, "IsPaneShow"), "return cancels running installation");
        handler.HoldDownloads = false;

        games.Data.Instances.Clear();
        ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(shader);
        vm = ((UserControl)Get(browser, "PaneContent")).DataContext!;
        Assert(!(bool)Get(vm, "HasInstances") && !((IAsyncRelayCommand)Get(vm, "ToInstallCommand")).CanExecute(null), "empty instance list guard");
        Capture(main, output, "shader-empty-instances.png", ThemeVariant.Light);
        main.Close();
        Console.WriteLine("PASS shared resource search/dropdown/pane, loader independence, task progress, terminal guards, cancellation/retry/failure, mod regression and runtime screenshots");
    }

    private static object? GetNullable(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);

    private static void Capture(Window window, string output, string name, ThemeVariant theme)
    {
        Application.Current!.RequestedThemeVariant = theme;
        var until = DateTime.UtcNow.AddMilliseconds(350);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(output, name));
    }

    // 使用真实 MainWindow 作为错误对话框 owner，跳过账户与在线页面初始化。
    private sealed class FixtureMainWindow : MainWindow
    {
        protected override void OnOpened(EventArgs e) { }
    }

    private sealed class ResourceHandler : HttpMessageHandler
    {
        private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("shader UI fixture");
        public List<Uri> Requests { get; } = [];
        public TaskCompletionSource SlowStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SlowRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ModStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldDownloads { get; set; }
        public bool HoldModMetadata { get; set; }
        public bool NoVersions { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            string json;
            if (uri.AbsolutePath == "/v2/search")
            {
                string query = Uri.UnescapeDataString(uri.Query);
                if (query.Contains("query=error&"))
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                if (query.Contains("query=slow&"))
                {
                    SlowStarted.TrySetResult();
                    await SlowRelease.Task; // 模拟旧请求较迟返回。
                }
                bool shader = query.Contains("project_type:shader");
                string title = query.Contains("query=latest&") ? "latest" : shader ? "光影测试项目" : "模组测试项目";
                string project = shader ? "shader-project" : "mod-project";
                json = query.Contains("query=empty&") ? "{\"hits\":[]}" : $$$"""
                    {"hits":[{"project_id":"{{{project}}}","title":"{{{title}}}","description":"用于验证资源检索与共享安装侧栏。","versions":["1.21.1","24w40a"],"display_categories":["fabric"],"date_created":"2026-10-23T00:00:00Z"}]}
                    """;
            }
            else if (uri.AbsolutePath is "/v2/project/shader-project/version" or "/v2/project/mod-project/version")
            {
                bool shader = uri.AbsolutePath.Contains("shader-project");
                if (!shader && HoldModMetadata)
                {
                    ModStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                string filename = shader ? "fixture.zip" : "fixture.jar";
                string dependencies = shader ? "{\"project_id\":\"iris-must-not-be-requested\"}" : "";
                string hash = Convert.ToHexStringLower(SHA1.HashData(Bytes));
                json = NoVersions ? "[]" : $$$"""
                    [{"files":[{"filename":"{{{filename}}}","url":"https://files.invalid/main","size":{{{Bytes.Length}}},"primary":true,"hashes":{"sha1":"{{{hash}}}"}}],"dependencies":[{{{dependencies}}}]}]
                    """;
            }
            else if (uri.Host == "files.invalid")
            {
                if (HoldDownloads)
                {
                    DownloadStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) };
            }
            else throw new Exception("Unexpected dependency request: " + uri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
