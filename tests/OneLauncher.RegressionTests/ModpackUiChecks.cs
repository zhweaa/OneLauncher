using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

internal static class ModpackUiChecks
{
    private static readonly Assembly Ui = typeof(MainWindow).Assembly;
    private static Type UiType(string name) => Ui.GetType(name, true)!;
    private static object Get(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
    private static void Set(object value, string name, object? property) => value.GetType().GetProperty(name)!.SetValue(value, property);
    private static Task Call(object value, string name) => (Task)value.GetType().GetMethod(name)!.Invoke(value, null)!;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Modpack fixture timed out");
            await Task.Delay(10);
        }
    }

    public static async Task RunAsync(string output)
    {
        string root = Path.Combine(output, "data", Guid.NewGuid().ToString("N"));
        Init.GameRootPath = Path.Combine(root, "minecraft");
        var config = new DBManager(Path.Combine(root, "config.json"));
        await config.InitializeAsync();
        typeof(Init).GetProperty("ConfigManager", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, config);
        var games = new GameDataManager(Path.Combine(root, "games.json"));
        await games.InitializeAsync();
        var accounts = new AccountManager(Path.Combine(root, "accounts.json"));
        await accounts.InitializeAsync();
        var versions = new VersionListManager(Init.GameRootPath);
        await versions.InitializeAsync();
        Directory.CreateDirectory(Path.Combine(Init.GameRootPath, "versions", "1.21.1"));
        await File.WriteAllTextAsync(Path.Combine(Init.GameRootPath, "versions", "1.21.1", "version.json"), "{}");
        await versions.RegisterInstalledVersionAsync(new UserVersion { VersionID = "1.21.1", modType = new() });
        var tasks = new TaskManager();
        var service = (ResourceInstallerService)Activator.CreateInstance(typeof(ResourceInstallerService), nonPublic: true)!;
        var core = (UnitedCore)RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
        Set(core, nameof(UnitedCore.GameDataManager), games);
        Set(core, nameof(UnitedCore.AccountManager), accounts);
        Set(core, nameof(UnitedCore.VersionListManager), versions);
        Set(core, nameof(UnitedCore.TaskManager), tasks);
        Set(core, nameof(UnitedCore.ResourceInstallerService), service);
        typeof(UnitedCore).GetProperty("Core", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, core);
        using var handler = new PackHandler();
        using var client = new HttpClient(handler);
        var downloadType = typeof(Init).Assembly.GetType("OneLauncher.Core.DefaultComponent.Downloader.Download", true)!;
        using var download = (IDisposable)Activator.CreateInstance(downloadType, client)!;
        typeof(Init).GetProperty("Download", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, download);

        var search = new SearchModrinth();
        await search.ToSearch("整合包 & ?", ResourceType.Modpack);
        string query = Uri.UnescapeDataString(handler.Requests[^1].Query);
        Assert(query.Contains("project_type:modpack") && !query.Contains("categories:"), "pack search ignores mod loader filters");
        Assert(handler.Requests[^1].Query.Contains("%26"), "pack keyword encoded");

        // 后端入口读取不可定位的网络流，解出清单；已取消调用不发起请求。
        using (var package = await service.GetModpackInfoAsync("pack-project"))
        {
            Assert(package.Parser.GetName() == "清单中的整合包" && package.Parser.GetMinecraftVersion() == "1.21.1", "preview uses mrpack manifest");
            Assert(package.Parser.GetLoaderInfo().Type == ModEnum.none && games.AllGameData.Count == 0, "preview never creates an instance");
        }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            int requests = handler.Requests.Count;
            try { await service.GetModpackInfoAsync("pack-project", canceled.Token); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { }
            Assert(handler.Requests.Count == requests, "pre-canceled preview does not request metadata");
        }

        Init.InitTask = Task.FromResult<IServiceCollection>(new ServiceCollection().AddSingleton(core).AddSingleton(config));
        var main = new FixtureMainWindow { Width = 880, Height = 620 };
        WeakReferenceMessenger.Default.UnregisterAll(main);
        var browserType = UiType("OneLauncher.Views.ViewModels.ModsBrowserViewModel");
        var browser = main.provider.GetRequiredService(browserType);
        var page = new ModsBrowser { DataContext = browser };
        main.Content = page;
        main.Show();
        await Until(() => !(bool)Get(browser, "IsSearching"));
        var selector = page.FindControl<ComboBox>("ResourceTypeSelector")!;
        selector.SelectedIndex = 2;
        await Until(() => !(bool)Get(browser, "IsSearching"));
        Assert((string)Get(browser, "SearchWatermark") == "搜索整合包...", "pack search placeholder");
        Capture(main, output, "pack-browser-light.png", ThemeVariant.Light);
        var resource = ((System.Collections.IList)Get(browser, "Items"))[0]!;
        using var iconServer = new IconServer();
        Set(resource, "IconUrl", iconServer.Url);
        var center = Activator.CreateInstance(UiType("OneLauncher.Views.Panes.PaneViewModels.TaskCenterPaneViewModel"), core, (Action)(() => { }))!;
        await Until(() => page.GetVisualDescendants().OfType<Button>().Any(button =>
            button.Classes.Contains("item-action") && ReferenceEquals(button.CommandParameter, resource)));
        var installButton = page.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("item-action"));
        installButton.Command!.Execute(installButton.CommandParameter);
        var pane = (UserControl)Get(browser, "PaneContent");
        var vm = pane.DataContext!;
        await Until(() => !(bool)Get(vm, "IsLoading"));
        Assert(pane.GetType().Name == "InstallPackPane" && (bool)Get(vm, "HasPackage"), "pack uses separate ready pane");
        Assert((string)Get(vm, "PackName") == "清单中的整合包" && (string)Get(vm, "LoaderInfo") == "原版", "pane displays parser data");
        Assert(games.AllGameData.Count == 0 && ((IAsyncRelayCommand)Get(vm, "ToInstallCommand")).CanExecute(null), "installation requires explicit click");
        Capture(main, output, "pack-pane-light.png", ThemeVariant.Light);
        Capture(main, output, "pack-pane-dark.png", ThemeVariant.Dark);
        main.Width = 760;
        main.Height = 500;
        Capture(main, output, "pack-pane-small.png", ThemeVariant.Light);
        main.Width = 880;
        main.Height = 620;
        string PackagePath(object value)
        {
            var package = value.GetType().GetField("_package", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
            return (string)package.GetType().GetProperty("FilePath", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(package)!;
        }
        string packagePath = PackagePath(vm);
        var originalPackage = File.ReadAllBytes(packagePath);
        int packDownloads = handler.PackDownloads;

        // 慢速清单文件可取消；取消后允许复用包文件重试，迟到回调不覆盖终态。
        handler.HoldFiles = true;
        var installing = Call(vm, "ToInstall");
        await Until(() => handler.FileStarted.Task.IsCompleted);
        await Call(vm, "ToInstall");
        Assert(tasks.TaskQueue.Count == 1, "duplicate click cannot create another task");
        var centerRows = (System.Collections.IList)Get(center, "Tasks");
        Assert(centerRows.Count == 1 && ReferenceEquals(Get(centerRows[0]!, "Task"), tasks.TaskQueue.Last()), "already-open task center receives pack installation");
        ((IRelayCommand)Get(vm, "CancelInstallCommand")).Execute(null);
        Assert((bool)Get(vm, "IsCancelling") && !((IRelayCommand)Get(vm, "CancelInstallCommand")).CanExecute(null), "cancel is visible and cannot repeat");
        await installing;
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Canceled && games.AllGameData.Count == 0, "cancel does not register an instance");
        Capture(main, output, "pack-canceled.png", ThemeVariant.Light);
        handler.HoldFiles = false;
        await Call(vm, "ToInstall");
        Dispatcher.UIThread.RunJobs();
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Finished && games.AllGameData.Count == 1, "retry registers new instance");
        var instance = games.AllGameData.Single();
        Assert(instance.Name == "清单中的整合包" && File.ReadAllBytes(Path.Combine(instance.InstancePath, "mods", "fixture.jar")).SequenceEqual(PackHandler.FileBytes), "manifest name and downloaded file installed");
        Assert(File.ReadAllText(Path.Combine(instance.InstancePath, "config", "fixture.txt")) == "override fixture", "overrides applied");
        Assert(File.ReadAllBytes(Path.Combine(instance.InstancePath, ".olc", "customicon")).SequenceEqual(IconServer.Png), "pane writes icon to returned instance");
        Assert(File.ReadAllBytes(packagePath).SequenceEqual(originalPackage), "icon never changes downloaded mrpack");
        var reloaded = new GameDataManager(Path.Combine(root, "games.json"));
        await reloaded.InitializeAsync();
        using (var bitmap = (IDisposable)UiType("OneLauncher.Views.ViewModels.GameDataItem").GetMethod("GetGameDataIcon")!.Invoke(null, [reloaded.AllGameData.Single()])!) { }
        Assert(handler.PackDownloads == packDownloads && (double)Get(vm, "CurrentProgress") == 100, "install reuses package and final progress stable");
        Assert(!((IAsyncRelayCommand)Get(vm, "ToInstallCommand")).CanExecute(null), "completed pane cannot import twice");
        Capture(main, output, "pack-completed.png", ThemeVariant.Light);
        var footerButtons = pane.GetVisualDescendants().OfType<Button>().Where(button =>
            ReferenceEquals(button.Command, Get(vm, "CancelInstallCommand")) || (button.Content as string == "完成")).ToArray();
        Assert(footerButtons.Length == 2 && Math.Abs(footerButtons[0].Bounds.Width - footerButtons[1].Bounds.Width) < 1 &&
            ReferenceEquals(footerButtons[0].Parent, footerButtons[1].Parent), "pack footer keeps cancel and complete in equal columns");
        ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);
        Assert(!File.Exists(packagePath) && !(bool)Get(browser, "IsPaneShow"), "close releases downloaded package");

        // 任务中心发起取消也会更新安装侧栏，并在退出后删除包文件。
        ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(resource);
        vm = ((UserControl)Get(browser, "PaneContent")).DataContext!;
        await Until(() => !(bool)Get(vm, "IsLoading"));
        packagePath = PackagePath(vm);
        handler.HoldFiles = true;
        handler.FileStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        installing = Call(vm, "ToInstall");
        await handler.FileStarted.Task;
        var externalCenter = Activator.CreateInstance(UiType("OneLauncher.Views.Panes.PaneViewModels.TaskCenterPaneViewModel"), core, (Action)(() => { }))!;
        var externalRow = ((System.Collections.IList)Get(externalCenter, "Tasks"))[0]!;
        ((IRelayCommand)Get(externalRow, "CancelTaskCommand")).Execute(null);
        ((IDisposable)externalCenter).Dispose();
        Assert((bool)Get(vm, "IsCancelling"), "external cancellation updates pack pane");
        ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);
        await installing;
        Assert(!File.Exists(packagePath) && games.AllGameData.Count == 1, "close cancels install and releases zip after exit");
        handler.HoldFiles = false;

        // 加载网络流期间关闭侧栏，响应令牌取消；重新打开后仍可获取信息。
        handler.HoldPackage = true;
        ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(resource);
        vm = ((UserControl)Get(browser, "PaneContent")).DataContext!;
        await handler.PackageStarted.Task;
        ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);
        await Until(() => !(bool)Get(vm, "IsLoading"));
        Assert((TaskInfoStatus)Get(vm, "InstallationStatus") == TaskInfoStatus.Canceled && !(bool)Get(vm, "HasPackage"), "closing preview cancels network request");
        handler.HoldPackage = false;

        // 错误信息进入失败状态，通过现有异常界面显示，重试后可恢复。
        handler.EmptyVersions = true;
        ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(resource);
        vm = ((UserControl)Get(browser, "PaneContent")).DataContext!;
        await Until(() => main.OwnedWindows.Count > 0);
        foreach (var dialog in main.OwnedWindows.ToArray()) dialog.Close();
        await Until(() => !(bool)Get(vm, "IsLoading"));
        Assert((TaskInfoStatus)Get(vm, "InstallationStatus") == TaskInfoStatus.Failed && (bool)Get(vm, "CanReload"), "preview failure supports retry");
        handler.EmptyVersions = false;
        await Call(vm, "LoadInfo");
        Assert((bool)Get(vm, "HasPackage"), "preview recovers");
        packagePath = PackagePath(vm);
        handler.FailFiles = true;
        installing = Call(vm, "ToInstall");
        await Until(() => main.OwnedWindows.Count > 0);
        foreach (var dialog in main.OwnedWindows.ToArray()) dialog.Close();
        await installing;
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Failed && games.AllGameData.Count == 1, "download failure never registers instance");
        Capture(main, output, "pack-failed.png", ThemeVariant.Dark);
        ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);
        Assert(!File.Exists(packagePath), "failed pack file released");

        // 可选图标的 HTTP 错误、无效图片及缺失都不能阻止导入。
        handler.FailFiles = false;
        foreach (var scenario in new[] { "http-failure", "invalid-image", "missing" })
        {
            iconServer.Mode = scenario;
            Set(resource, "IconUrl", scenario == "missing" ? null : iconServer.Url);
            ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(resource);
            vm = ((UserControl)Get(browser, "PaneContent")).DataContext!;
            await Until(() => !(bool)Get(vm, "IsLoading"));
            await Call(vm, "ToInstall");
            Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Finished, "optional icon failure preserves successful import: " + scenario);
            var created = games.AllGameData.Last();
            Assert(!File.Exists(Path.Combine(created.InstancePath, ".olc", "customicon")), "failed or missing icon leaves default intact");
            ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);
        }
        iconServer.Mode = "hold";
        Set(resource, "IconUrl", iconServer.Url);
        int registered = games.AllGameData.Count;
        ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(resource);
        vm = ((UserControl)Get(browser, "PaneContent")).DataContext!;
        await Until(() => !(bool)Get(vm, "IsLoading"));
        int iconRequests = iconServer.Requests;
        installing = Call(vm, "ToInstall");
        await Until(() => iconServer.Requests > iconRequests);
        ((IRelayCommand)Get(vm, "CancelInstallCommand")).Execute(null);
        await installing;
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Canceled && games.AllGameData.Count == registered,
            "canceling icon fetch does not create an instance");
        ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);

        // 在图标完成后的刷新事件中取消，不能让取消状态被 Done 覆盖或允许再次创建实例。
        iconServer.Mode = "success";
        int saves = 0;
        void CancelAfterIconSaved()
        {
            if (++saves == 2) tasks.TaskQueue.Last().Cancel();
        }
        games.OnDataChanged += CancelAfterIconSaved;
        ((IRelayCommand)Get(browser, "ToInstallResourceCommand")).Execute(resource);
        vm = ((UserControl)Get(browser, "PaneContent")).DataContext!;
        await Until(() => !(bool)Get(vm, "IsLoading"));
        await Call(vm, "ToInstall");
        games.OnDataChanged -= CancelAfterIconSaved;
        Assert(tasks.TaskQueue.Last().Status == TaskInfoStatus.Canceled && games.AllGameData.Count == registered + 1 &&
            (bool)Get(vm, "IsCompleted") && !((IAsyncRelayCommand)Get(vm, "ToInstallCommand")).CanExecute(null),
            "late cancellation keeps committed instance, preserves canceled status and prevents duplicate import");
        ((IRelayCommand)Get(vm, "ClosePaneCommand")).Execute(null);

        // 两个侧栏各自从返回的实例保存图标，不能按名称或全局最后实例猜测目标。
        var firstResource = Activator.CreateInstance(resource.GetType())!;
        var secondResource = Activator.CreateInstance(resource.GetType())!;
        foreach (var item in new[] { firstResource, secondResource })
        {
            Set(item, "ID", "pack-project");
            Set(item, "Title", "相同名称");
            Set(item, "Type", ResourceType.Modpack);
        }
        Set(firstResource, "IconUrl", iconServer.Url);
        Set(secondResource, "IconUrl", new Uri(iconServer.Url + "?blue"));
        var paneType = UiType("OneLauncher.Views.Panes.PaneViewModels.InstallPackPaneViewModel");
        var firstPane = Activator.CreateInstance(paneType, firstResource, core, (Action)(() => { }))!;
        var secondPane = Activator.CreateInstance(paneType, secondResource, core, (Action)(() => { }))!;
        await Task.WhenAll(Call(firstPane, "LoadInfo"), Call(secondPane, "LoadInfo"));
        await Task.WhenAll(Call(firstPane, "ToInstall"), Call(secondPane, "ToInstall"));
        foreach (var pair in new[] { (firstPane, IconServer.Png), (secondPane, IconServer.BluePng) })
        {
            var created = (GameData)paneType.GetField("_importedInstance", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pair.Item1)!;
            Assert(File.ReadAllBytes(Path.Combine(created.InstancePath, ".olc", "customicon")).SequenceEqual(pair.Item2),
                "concurrent installs preserve each returned instance's own icon");
            ((IRelayCommand)Get(pair.Item1, "ClosePaneCommand")).Execute(null);
        }
        ((IDisposable)center).Dispose();
        main.Close();
        Console.WriteLine("PASS modpack preview/import, instance icons and restart, unchanged package, optional icon failure, concurrent attribution, task center, early/late cancellation, terminal guards and screenshots");
    }

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

    private sealed class FixtureMainWindow : MainWindow
    {
        protected override void OnOpened(EventArgs e) { }
    }

    private sealed class PackHandler : HttpMessageHandler
    {
        public static readonly byte[] FileBytes = Encoding.UTF8.GetBytes("modpack mod fixture");
        private readonly byte[] _package = CreatePackage();
        public List<Uri> Requests { get; } = [];
        public int PackDownloads { get; private set; }
        public bool HoldFiles { get; set; }
        public bool HoldPackage { get; set; }
        public bool EmptyVersions { get; set; }
        public bool FailFiles { get; set; }
        public TaskCompletionSource FileStarted { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PackageStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            if (uri.AbsolutePath == "/v2/search")
                return Json("""{"hits":[{"project_id":"pack-project","title":"搜索中的整合包","description":"验证整合包信息、安装进度与取消。","versions":["1.21.1"],"date_created":"2026-10-23T00:00:00Z"}]}""");
            if (uri.AbsolutePath == "/v2/project/pack-project/version")
                return Json(EmptyVersions ? "[]" : $$$"""
                    [{"files":[{"filename":"pack.mrpack","url":"https://fixture.invalid/pack","primary":true,"size":{{{_package.Length}}},"hashes":{"sha1":"{{{Convert.ToHexStringLower(SHA1.HashData(_package))}}}"}}]}]
                    """);
            if (uri.AbsolutePath == "/pack")
            {
                PackDownloads++;
                if (HoldPackage)
                {
                    PackageStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                return new(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableStream(_package)) };
            }
            if (uri.AbsolutePath == "/mod")
            {
                FileStarted.TrySetResult();
                if (HoldFiles) await Task.Delay(Timeout.Infinite, cancellationToken);
                return new(FailFiles ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new ByteArrayContent(FileBytes) };
            }
            throw new Exception("Unexpected request " + uri);
        }

        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
        { Content = new StringContent(text, Encoding.UTF8, "application/json") };

        private static byte[] CreatePackage()
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
                    writer.Write($$$"""
                        {"formatVersion":1,"game":"minecraft","versionId":"fixture","name":"清单中的整合包","dependencies":{"minecraft":"1.21.1"},"files":[{"path":"mods/fixture.jar","downloads":["https://fixture.invalid/mod"],"fileSize":{{{FileBytes.Length}}},"hashes":{"sha1":"{{{Convert.ToHexStringLower(SHA1.HashData(FileBytes))}}}"}}]}
                        """);
                using var overrides = new StreamWriter(zip.CreateEntry("overrides/config/fixture.txt").Open());
                overrides.Write("override fixture");
            }
            return stream.ToArray();
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    private sealed class IconServer : IDisposable
    {
        public static readonly byte[] Png = CreatePng(Brushes.Green);
        public static readonly byte[] BluePng = CreatePng(Brushes.Blue);
        private static byte[] CreatePng(IBrush color)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(16, 16), new Vector(96, 96));
            using (var drawing = bitmap.CreateDrawingContext()) drawing.DrawRectangle(color, null, new Rect(0, 0, 16, 16));
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task accept;
        private int requests;
        public int Requests => Volatile.Read(ref requests);
        public string Mode { get; set; } = "success";
        public Uri Url { get; }
        public IconServer()
        {
            listener.Start();
            Url = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/icon.png");
            accept = AcceptAsync();
        }
        private async Task AcceptAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                    _ = RespondAsync(client);
                }
            }
            catch (Exception) when (stop.IsCancellationRequested) { }
        }
        private async Task RespondAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var requestLine = await reader.ReadLineAsync(stop.Token);
                    while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
                    var mode = Mode;
                    Interlocked.Increment(ref requests);
                    if (mode == "hold") await Task.Delay(Timeout.Infinite, stop.Token);
                    var bytes = mode == "invalid-image" ? Encoding.UTF8.GetBytes("invalid png") :
                        requestLine?.Contains("?blue") == true ? BluePng : Png;
                    var status = mode == "http-failure" ? "404 Not Found" : "200 OK";
                    var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: image/png\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, stop.Token);
                    await stream.WriteAsync(bytes, stop.Token);
                }
                catch (Exception ex) when (stop.IsCancellationRequested || ex is IOException) { }
            }
        }
        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
            accept.GetAwaiter().GetResult();
        }
    }
}
