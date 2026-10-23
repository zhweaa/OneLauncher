using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Views;
using OneLauncher.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Services;

// Standalone offline regression runner: never initializes the real launcher or its user data.
var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("artifacts", "settings-regression"));
Directory.CreateDirectory(output);
var root = Path.Combine(output, "data", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

if (args.Contains("--launch-directory"))
{
    await LaunchWorkingDirectoryChecks.RunAsync(output);
    return;
}

if (args.Contains("--download-ui"))
{
    AppBuilder.Configure<OneLauncher.App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();
    var downloadTask = DownloadPaneUiChecks.RunAsync(output);
    while (!downloadTask.IsCompleted)
        Dispatcher.UIThread.RunJobs();
    await downloadTask;
    return;
}

if (args.Contains("--modpack-ui"))
{
    AppBuilder.Configure<OneLauncher.App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();
    var packTask = ModpackUiChecks.RunAsync(output);
    while (!packTask.IsCompleted)
        Dispatcher.UIThread.RunJobs();
    await packTask;
    return;
}

if (args.Contains("--shader-ui"))
{
    AppBuilder.Configure<OneLauncher.App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();
    var shaderTask = ShaderUiChecks.RunAsync(output);
    while (!shaderTask.IsCompleted)
        Dispatcher.UIThread.RunJobs();
    await shaderTask;
    return;
}

if (args.Contains("--notification-click"))
{
    AppBuilder.Configure<OneLauncher.App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();
    var notificationTask = CheckNotificationClick();
    while (!notificationTask.IsCompleted)
        Dispatcher.UIThread.RunJobs();
    await notificationTask;
    return;
}

await CheckLegacyVersionListIgnored();
await CheckVersionIndex();
await CheckSharedManagers();
await CheckInstallationGuards();

AppBuilder.Configure<OneLauncher.App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var uiTask = CheckSettingsUi();
while (!uiTask.IsCompleted)
    Dispatcher.UIThread.RunJobs();
await uiTask;
var shellTask = CheckNavigationShell();
while (!shellTask.IsCompleted)
    Dispatcher.UIThread.RunJobs();
await shellTask;
Console.WriteLine("PASS: ignored legacy version list, version index, shared saves, settings bindings and flyout lifecycle");

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

async Task ExpectFailure(Func<Task> action, string path)
{
    try { await action(); }
    catch (OlanException ex)
    {
        Assert(ex.Message.Contains(path), "Failure must identify the actual data path");
        return;
    }
    throw new InvalidOperationException("Expected a data error");
}

async Task CheckLegacyVersionListIgnored()
{
    var scenario = Path.Combine(root, "ignored-legacy-version-list");
    var configPath = Path.Combine(scenario, "config.json");
    var gameRoot = Path.Combine(scenario, "custom-installed", "minecraft");
    Directory.CreateDirectory(scenario);
    var metadata = Path.Combine(gameRoot, "versions", "1.20.1", "version.json");
    Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
    await File.WriteAllTextAsync(metadata, "{}");

    // Ignore the old field regardless of its shape, while preserving unrelated settings.
    foreach (var legacyJson in new[] { "[{\"VersionID\":\"1.20.1\"}]", "null", "{}", "42", "\"obsolete\"" })
    {
        var original = "{\"VersionList\":" + legacyJson +
            ",\"OlanSettings\":{\"LeftButtonListSpacing\":17},\"AvailableJavas\":{\"21\":\"java\"}}";
        await File.WriteAllTextAsync(configPath, original);
        var config = new DBManager(configPath);
        await config.InitializeAsync();
        Assert(config.Data.OlanSettings.LeftButtonListSpacing == 17 && config.Data.AvailableJavas[21] == "java",
            "Ignoring the legacy version list must preserve global settings");
        Assert(await File.ReadAllTextAsync(configPath) == original,
            "Reading config must not trigger migration or rewrite it");
        await config.Save();
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        Assert(!saved.RootElement.TryGetProperty("VersionList", out _) &&
            saved.RootElement.GetProperty("OlanSettings").GetProperty("LeftButtonListSpacing").GetInt32() == 17,
            "Saving config must omit the ignored field and preserve settings");
    }

    var obsoleteConfig = "{\"VersionList\":[{\"VersionID\":\"1.20.1\"}],\"OlanSettings\":{\"LeftButtonListSpacing\":17}}";
    await File.WriteAllTextAsync(configPath, obsoleteConfig);
    var versions = new VersionListManager(gameRoot);
    await versions.InitializeAsync();
    Assert(versions.GetVersions().Count == 0, "Missing index must initialize empty without importing config or scanning versions");
    var date = new DateTime(2025, 1, 2, 3, 4, 5);
    await versions.RegisterInstalledVersionAsync(new() { VersionID = "1.21.1", AddTime = date });
    var indexBytes = await File.ReadAllTextAsync(versions.IndexPath);
    var restored = new VersionListManager(gameRoot);
    await restored.InitializeAsync();
    Assert(restored.GetVersions().Single() is { VersionID: "1.21.1" } &&
        await File.ReadAllTextAsync(restored.IndexPath) == indexBytes,
        "An existing index must remain authoritative and unchanged on initialization");
    var empty = new VersionListManager(Path.Combine(scenario, "other-root"));
    await empty.InitializeAsync();
    Assert(empty.GetVersions().Count == 0 && restored.GetVersions().Count == 1,
        "Version indexes must remain isolated by installation root");
    Assert(await File.ReadAllTextAsync(configPath) == obsoleteConfig,
        "Initializing missing or existing indexes must leave global config untouched");
    Assert(!Directory.GetFiles(scenario, "config.pre-version-list-migration.*.json").Any(),
        "Initialization must not create migration backups");

    foreach (var text in new[] { "", "null", "{}", "{", "{\"VersionList\":null}",
        "{\"VersionList\":[{\"VersionID\":\"../escape\"}]}" })
    {
        var broken = new VersionListManager(Path.Combine(scenario, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.GetDirectoryName(broken.IndexPath)!);
        await File.WriteAllTextAsync(broken.IndexPath, text);
        await ExpectFailure(() => broken.InitializeAsync(), broken.IndexPath);
        Assert(await File.ReadAllTextAsync(broken.IndexPath) == text,
            "Invalid index must not be overwritten");
    }
    Console.WriteLine("PASS: ignored legacy version list, independent index initialization, invalid documents, root isolation");
}

async Task CheckVersionIndex()
{
    var manager = new VersionListManager(Path.Combine(root, "concurrent"));
    await manager.InitializeAsync();
    int events = 0;
    manager.OnDataChanged += () =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manager.IndexPath));
        Assert(document.RootElement.GetProperty("VersionList").GetArrayLength() > 0,
            "Event must observe a committed complete file");
        events++;
    };
    await Task.WhenAll(
        manager.RegisterInstalledVersionAsync(new() { VersionID = "1.20.1", modType = new() { IsFabric = true } }),
        manager.RegisterInstalledVersionAsync(new() { VersionID = "1.20.1", modType = new() { IsForge = true } }));
    Assert(manager.GetVersions().Single().modType is { IsFabric: true, IsForge: true } && events == 2,
        "Concurrent registrations must merge without duplicates");
    var snapshot = manager.GetVersions();
    snapshot[0].VersionID = "changed";
    Assert(manager.FindVersion("1.20.1") != null, "UI snapshot must not mutate stored versions");
    var oldOrder = manager.GetVersions();
    await manager.RegisterInstalledVersionAsync(new() { VersionID = "1.21.1" });
    await manager.ReplaceOrderAsync(oldOrder);
    var reload = new VersionListManager(manager.GameRootPath);
    await reload.InitializeAsync();
    Assert(reload.GetVersions().Select(v => v.VersionID).SequenceEqual(new[] { "1.20.1", "1.21.1" }),
        "Sorting a stale snapshot must preserve new registrations");
    var count = events;
    using (File.Open(manager.IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await ExpectFailure(() => manager.RegisterInstalledVersionAsync(new() { VersionID = "1.22" }), manager.IndexPath);
    Assert(events == count && manager.FindVersion("1.22") == null, "Failed commit must publish neither state nor event");
    Assert(!Directory.GetFiles(Path.GetDirectoryName(manager.IndexPath)!, "*.tmp").Any(), "Failed save must clean its temporary file");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try { await manager.RegisterInstalledVersionAsync(new() { VersionID = "cancelled" }, cancelled.Token); }
    catch (OperationCanceledException) { }
    Assert(manager.FindVersion("cancelled") == null, "Cancelled registration must not publish a version");
    Console.WriteLine("PASS: concurrent registration, immutable snapshots, sorting persistence, failed/cancelled commit");
}

async Task CheckSharedManagers()
{
    var path = Path.Combine(root, "shared");
    var config = new DBManager(Path.Combine(path, "config.json"));
    await config.InitializeAsync();
    Assert(config.Data.OlanSettings is { IsServerPageVisible: true, IsVersionPageVisible: false,
        IsGameDataPageVisible: true, IsDownloadPageVisible: true, IsModsBrowserPageVisible: true, IsAccountPageVisible: true },
        "New navigation defaults must match the requested visibility");
    var oldSettings = JsonSerializer.Deserialize("{\"IsServerPageVisible\":false,\"IsVersionPageVisible\":true}",
        OneLauncherAppConfigsJsonContext.Default.AppSettings)!;
    Assert(!oldSettings.IsServerPageVisible && oldSettings.IsVersionPageVisible && oldSettings.IsAccountPageVisible,
        "Explicit legacy choices must survive while missing new fields get defaults");
    int configEvents = 0;
    config.OnDataChanged += () => { using var _ = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "config.json"))); configEvents++; };
    config.Data.OlanSettings.LeftButtonListSpacing = 19;
    await config.Save();
    var reload = new DBManager(Path.Combine(path, "config.json"));
    await reload.InitializeAsync();
    Assert(configEvents == 1 && reload.Data.OlanSettings.LeftButtonListSpacing == 19, "Global settings save regression");
    var accounts = new AccountManager(Path.Combine(path, "accounts.json"));
    await accounts.InitializeAsync();
    await accounts.Save();
    var accountReload = new AccountManager(Path.Combine(path, "accounts.json"));
    await accountReload.InitializeAsync();
    Assert(accountReload.GetAllUsers().Any(), "Account save regression");
    var games = new GameDataManager(Path.Combine(path, "games.json"));
    await games.InitializeAsync();
    Assert(games.GetDefaultInstance() == null, "Empty game data must safely return no default instance");
    await games.Save();
    var gameReload = new GameDataManager(Path.Combine(path, "games.json"));
    await gameReload.InitializeAsync();
    Assert(gameReload.AllGameData.Count == 0, "Game data save regression");
    Console.WriteLine("PASS: global settings compatibility, event timing, accounts and empty game-data saves");
}

async Task CheckSettingsUi()
{
    var config = new DBManager(Path.Combine(root, "ui", "config.json"));
    await config.InitializeAsync();
    var games = new GameDataManager(Path.Combine(root, "ui", "games.json"));
    await games.InitializeAsync();
    var core = (UnitedCore)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
    typeof(UnitedCore).GetProperty(nameof(UnitedCore.GameDataManager))!.SetValue(core, games);
    var vmType = typeof(settings).Assembly.GetType("OneLauncher.Views.ViewModels.SettingsPageViewModel", true)!;
    var vm = Activator.CreateInstance(vmType, config, core)!;
    object Get(string name) => vmType.GetProperty(name)!.GetValue(vm)!;
    void Set(string name, object value) => vmType.GetProperty(name)!.SetValue(vm, value);
    var page = new settings { DataContext = vm };
    var window = new Window { Width = 698, Height = 470, Content = page,
        Background = Avalonia.Media.Brushes.White, RequestedThemeVariant = ThemeVariant.Light };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    var combo = page.FindControl<ComboBox>("SettingsCategoryComboBox")!;
    var sections = page.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("settings-section")).ToArray();
    Assert(sections.Length == 5, "Settings page must contain four independent sections plus About");
    for (var i = 0; i < combo.ItemCount; i++)
    {
        combo.SelectedIndex = i;
        Dispatcher.UIThread.RunJobs();
        for (var section = 0; section < sections.Length; section++)
            Assert(sections[section].IsVisible == (i == 0 || section == i - 1 || section == 4),
                $"Incorrect section visibility for filter {i}, section {section}");
    }
    combo.SelectedIndex = 1;
    Dispatcher.UIThread.RunJobs();
    var button = page.FindControl<Button>("NavigationVisibilityButton")!;
    var flyout = (SettingsFlyout)button.Flyout!;
    flyout.ShowAt(button);
    Dispatcher.UIThread.RunJobs();
    Assert((bool)Get("IsNavigationEditing"), "Button.Flyout lifecycle must update the view model");
    var grid = (Grid)flyout.Content!;
    var checkboxes = grid.GetVisualDescendants().OfType<CheckBox>().ToArray();
    Assert(checkboxes.Length == 6, "Flyout must generate six bound checkboxes");
    var versionBox = checkboxes.Single(c => Equals(c.Content, "版本管理"));
    versionBox.IsChecked = true;
    Assert(!config.Data.OlanSettings.IsVersionPageVisible, "Checkbox editing must not write live config");
    flyout.Hide();
    Dispatcher.UIThread.RunJobs();
    flyout.ShowAt(button);
    Dispatcher.UIThread.RunJobs();
    checkboxes = grid.GetVisualDescendants().OfType<CheckBox>().ToArray();
    Assert(checkboxes.Single(c => Equals(c.Content, "版本管理")).IsChecked == false,
        "Reopening must discard unsaved changes");
    foreach (var check in checkboxes) check.IsChecked = false;
    await ((IAsyncRelayCommand)Get("SaveNavigationCommand")).ExecuteAsync(null);
    Dispatcher.UIThread.RunJobs();
    Assert(!flyout.IsOpen && !(bool)Get("IsNavigationEditing"), "Successful save must close the native button flyout");
    var reload = new DBManager(Path.Combine(root, "ui", "config.json"));
    await reload.InitializeAsync();
    Assert(reload.Data.OlanSettings is { IsVersionPageVisible: false, IsServerPageVisible: false,
        IsGameDataPageVisible: false, IsDownloadPageVisible: false, IsModsBrowserPageVisible: false, IsAccountPageVisible: false },
        "All six unchecked navigation choices must survive reload");

    flyout.ShowAt(button);
    Dispatcher.UIThread.RunJobs();
    checkboxes = grid.GetVisualDescendants().OfType<CheckBox>().ToArray();
    checkboxes.Single(c => Equals(c.Content, "服务器收藏")).IsChecked = true;
    await ((IAsyncRelayCommand)Get("SaveNavigationCommand")).ExecuteAsync(null);
    Assert(config.Data.OlanSettings.IsServerPageVisible && config.Data.ServerList.Count == 0,
        "Enabling servers without a game instance must save without inventing a server");

    flyout.ShowAt(button);
    Dispatcher.UIThread.RunJobs();
    Set("IsSavingNavigation", true);
    flyout.Hide();
    Assert(flyout.IsOpen, "Busy save must prevent accidental dismissal");
    Set("IsSavingNavigation", false);
    flyout.Hide();

    combo.SelectedIndex = 1;
    Settle();
    using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "settings-light-minimum.png"));
    window.RequestedThemeVariant = ThemeVariant.Dark;
    window.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1D1E21"));
    flyout.ShowAt(button);
    Settle();
    using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "settings-dark-flyout.png"));
    flyout.Hide();
    combo.SelectedIndex = 4;
    Settle();
    using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "settings-runtime.png"));
    window.Close();
    Console.WriteLine("PASS: all five filters, About always visible, flyout binding/reset/save, persisted visibility, no-instance server save");
}

async Task CheckInstallationGuards()
{
    var manager = new VersionListManager(Path.Combine(root, "install-guards"));
    await manager.InitializeAsync();
    var service = (VersionInstallerService)Activator.CreateInstance(typeof(VersionInstallerService),
        BindingFlags.NonPublic | BindingFlags.Instance, null, [manager], null)!;
    var info = (DownloadInfo)Activator.CreateInstance(typeof(DownloadInfo), nonPublic: true)!;
    void SetInfoRoot(string value) => typeof(DownloadInfo).GetProperty(nameof(DownloadInfo.GameRootPath))!.SetValue(info, value);
    var progress = new InlineProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>(_ =>
        throw new InvalidOperationException("Rejected install must not start or report completion"));
    SetInfoRoot(Path.Combine(root, "wrong-install-root"));
    await ExpectFailure(() => service.InstallNewVersion(info, progress), "wrong-install-root");
    SetInfoRoot(manager.GameRootPath);
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    try { await service.InstallNewVersion(info, progress, cancel.Token); }
    catch (OperationCanceledException) { }
    Assert(manager.GetVersions().Count == 0, "Rejected or cancelled install must not register any version");

    var core = (UnitedCore)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
    typeof(UnitedCore).GetProperty(nameof(UnitedCore.VersionListManager))!.SetValue(core, manager);
    typeof(UnitedCore).GetProperty("Core", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, core);
    // All other services are intentionally absent: these calls must fail before metadata/network activity.
    await ExpectFailure(() => DownloadInfo.Create("1.20.1", new(), gameRootPathD: Path.Combine(root, "wrong-metadata-root")),
        "wrong-metadata-root");
    try { await DownloadInfo.Create("../escape", new(), gameRootPathD: manager.GameRootPath); }
    catch (OlanException ex) { Assert(ex.Title == "版本安装失败", "Invalid version ID must be rejected before downloading"); }
    Console.WriteLine("PASS: installer cancellation, directory mismatch, metadata rejected before network activity");
}

void Settle()
{
    // Let opacity/brush animations finish before taking the runtime render.
    var until = DateTime.UtcNow.AddMilliseconds(350);
    while (DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

async Task CheckNavigationShell()
{
    var config = new DBManager(Path.Combine(root, "shell", "config.json"));
    await config.InitializeAsync();
    typeof(Init).GetProperty("ConfigManager", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, config);
    var services = new ServiceCollection();
    services.AddSingleton(config);
    var core = (UnitedCore)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
    var games = new GameDataManager(Path.Combine(root, "shell", "games.json"));
    await games.InitializeAsync();
    var accounts = new AccountManager(Path.Combine(root, "shell", "accounts.json"));
    await accounts.InitializeAsync();
    var versions = new VersionListManager(Path.Combine(root, "shell", "minecraft"));
    await versions.InitializeAsync();
    void SetCore(string name, object value) => typeof(UnitedCore).GetProperty(name)!.SetValue(core, value);
    SetCore(nameof(UnitedCore.GameDataManager), games);
    SetCore(nameof(UnitedCore.AccountManager), accounts);
    SetCore(nameof(UnitedCore.VersionListManager), versions);
    SetCore(nameof(UnitedCore.MojangVersionList), new List<VersionBasicInfo>());
    services.AddSingleton(core);
    Init.InitTask = Task.FromResult<IServiceCollection>(services);
    var main = new MainWindow();
    using (var validated = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))
    {
        // Validate the exact registrations MainWindow added, including dependencies of all pages.
        foreach (var descriptor in services.Where(d => d.ServiceType.Namespace ==
            "OneLauncher.ViewModel.PaneViewModels.Factories"))
            Assert(validated.GetRequiredService(descriptor.ServiceType) != null, $"Factory resolution failed: {descriptor.ServiceType}");
        foreach (var pageName in new[] { "VersionPageViewModel", "AccountPageViewModel", "SettingsPageViewModel",
            "GameDataPageViewModel", "DownloadPageViewModel", "ServerPageViewModel" })
        {
            var pageType = typeof(MainWindow).Assembly.GetType("OneLauncher.Views.ViewModels." + pageName, true)!;
            Assert(validated.GetRequiredService(pageType) != null, "Page resolution failed: " + pageName);
        }
        var factoryType = typeof(MainWindow).Assembly.GetType(
            "OneLauncher.ViewModel.PaneViewModels.Factories.PowerPlayPaneViewModelFactory", true)!;
        var factory = validated.GetRequiredService(factoryType);
        Assert(ReferenceEquals(factoryType.GetField("_gameDataManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(factory), games),
            "PowerPlay factory must reuse the exact UnitedCore game data manager");
    }
    Console.WriteLine("PASS: complete startup DI graph, all nine pane factories, six offline page constructors, shared manager identity");
    var names = new[] { "VersionListBoxItem", "GameDataListBoxItem", "ServerListBoxItem",
        "DownloadListBoxItem", "ModsBrowserListBoxItem", "AccountListBoxItem" };
    Assert(names.Select(name => main.FindControl<ListBoxItem>(name)!.IsVisible)
        .SequenceEqual(new[] { false, true, true, true, true, true }), "Shell must apply default visibility");
    main.HomePage = new OneLauncher.Home();
    main.versionPage = new OneLauncher.Views.version();
    var content = main.FindControl<TransitioningContentControl>("PageContent")!;
    var list = main.FindControl<ListBox>("SplitListBox")!;
    config.Data.OlanSettings.IsVersionPageVisible = true;
    var apply = typeof(MainWindow).GetMethod("ApplyUiSettings", BindingFlags.NonPublic | BindingFlags.Instance)!;
    apply.Invoke(main, null);
    list.SelectedItem = main.FindControl<ListBoxItem>("VersionListBoxItem");
    Assert(ReferenceEquals(content.Content, main.versionPage), "Selecting visible navigation item must show its page");
    config.Data.OlanSettings.IsVersionPageVisible = false;
    config.Data.OlanSettings.IsGameDataPageVisible = false;
    config.Data.OlanSettings.IsServerPageVisible = false;
    config.Data.OlanSettings.IsDownloadPageVisible = false;
    config.Data.OlanSettings.IsModsBrowserPageVisible = false;
    config.Data.OlanSettings.IsAccountPageVisible = false;
    apply.Invoke(main, null);
    Assert(names.All(name => !main.FindControl<ListBoxItem>(name)!.IsVisible), "All optional navigation items must hide");
    Assert(main.FindControl<ListBoxItem>("HomeListBoxItem")!.IsVisible &&
        main.FindControl<ListBoxItem>("SettingsListBoxItem")!.IsVisible &&
        ReferenceEquals(content.Content, main.HomePage) &&
        ReferenceEquals(list.SelectedItem, main.FindControl<ListBoxItem>("HomeListBoxItem")),
        "Hiding the current page must return to the permanent home entry");
    Console.WriteLine("PASS: actual navigation shell defaults, all optional items hidden, home/settings permanent, current-page fallback");
}

async Task CheckNotificationClick()
{
    var config = new DBManager(Path.Combine(root, "notification", "config.json"));
    await config.InitializeAsync();
    typeof(Init).GetProperty("ConfigManager", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, config);
    var tasks = new TaskManager();
    var core = (UnitedCore)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
    typeof(UnitedCore).GetProperty(nameof(UnitedCore.TaskManager))!.SetValue(core, tasks);
    typeof(UnitedCore).GetProperty(nameof(UnitedCore.MojangVersionList))!.SetValue(core, new List<VersionBasicInfo>());
    var services = new ServiceCollection();
    services.AddSingleton(config);
    services.AddSingleton(core);
    Init.InitTask = Task.FromResult<IServiceCollection>(services);
    var main = new MainWindow();
    var content = main.FindControl<TransitioningContentControl>("PageContent")!;
    var vmType = typeof(MainWindow).Assembly.GetType("OneLauncher.Views.ViewModels.DownloadPageViewModel", true)!;
    var vm = main.provider.GetRequiredService(vmType);
    content.Content = new download { DataContext = vm };
    var manager = main.FindControl<Avalonia.Controls.Notifications.WindowNotificationManager>("NotificationManager")!;
    manager.ApplyTemplate();

    Avalonia.Controls.Notifications.Notification Send(bool open)
    {
        main.ShowFlyout("Regression", "Notification click", openTaskCenterAfterClick: open);
        Dispatcher.UIThread.RunJobs();
        var cards = (System.Collections.IList)manager.GetType()
            .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        return (Avalonia.Controls.Notifications.Notification)((ContentControl)cards[cards.Count - 1]!).Content!;
    }
    object? Pane() => vmType.GetProperty("PaneContent")!.GetValue(vm);
    bool IsOpen() => (bool)vmType.GetProperty("IsPaneShow")!.GetValue(vm)!;
    int Subscriptions() => ((Delegate?)typeof(TaskManager)
        .GetField("OnTaskCreate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tasks))?.GetInvocationList().Length ?? 0;

    var ordinary = Send(false);
    Assert(ordinary.OnClick == null && Pane() == null, "Ordinary notification must not open task center");
    var notification = Send(true);
    Assert(notification.OnClick != null, "Task notification must supply an instance callback");
    notification.OnClick!();
    var firstPane = (UserControl)Pane()!;
    Assert(IsOpen() && firstPane.DataContext?.GetType().Name == "TaskCenterPaneViewModel" && Subscriptions() == 1,
        "Click on download View must open its ViewModel task pane without delegate errors");
    var firstVm = firstPane.DataContext!;
    var firstClose = (Action)firstVm.GetType().GetProperty("OnPaneClose")!.GetValue(firstVm)!;
    notification.OnClick();
    var secondPane = (UserControl)Pane()!;
    Assert(!ReferenceEquals(firstPane, secondPane) && Subscriptions() == 1, "Repeated click must release previous task subscriptions");
    firstClose();
    Assert(IsOpen(), "Stale close callback must not close the replacement pane");
    var secondVm = secondPane.DataContext!;
    var closeCommand = (IRelayCommand)secondVm.GetType().GetProperty("ClosePaneCommand")!.GetValue(secondVm)!;
    closeCommand.Execute(null);
    Assert(!IsOpen() && Subscriptions() == 0, "Close command must close pane and release subscriptions");
    closeCommand.Execute(null);
    content.Content = new download();
    notification.OnClick();
    Assert(Subscriptions() == 0, "Page without IPageHost DataContext must safely ignore notification click");
    content.Content = null;
    notification.OnClick();
    Assert(Subscriptions() == 0, "Missing page must safely ignore notification click");
    Console.WriteLine("PASS: notification click on download, repeated open, stale callback, close command, null host, subscriptions");
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
