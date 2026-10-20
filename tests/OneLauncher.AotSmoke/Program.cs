using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using System.Runtime.InteropServices;
using OneLauncher.Views.Controls;

// No desktop lifetime: loads the real App.axaml/styles but never MainWindow,
// Init, user configs, network login or game launching. Safe on CI without a display.
AppBuilder.Configure<OneLauncher.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

ImportTaskRegressionTests.Run(args.Length == 1 ? args[0] : null);

var assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}

var content = new Button { Content = "content" };
var paneContent = new Button { Content = "pane" };
var shell = new PageShell { Content = content, PaneContent = paneContent };
var first = new TextBox { Text = "first" };
var second = new TextBox { Text = "second" };
var panel = new StackPanel { Children = { shell, first, second } };
var window = new Window { Content = panel, Width = 800, Height = 600 };
window.Show();
Dispatcher.UIThread.RunJobs();

var split = shell.GetVisualDescendants().OfType<SplitView>().Single();
Check(!split.IsPaneOpen && content.IsEffectivelyEnabled, "PageShell initial state");
shell.IsPaneOpen = true;
Dispatcher.UIThread.RunJobs();
Check(split.IsPaneOpen && !content.IsEffectivelyEnabled, "compiled pane binding and disabled content");
Check(paneContent.IsEffectivelyEnabled, "pane content remains enabled");
split.SetCurrentValue(SplitView.IsPaneOpenProperty, false);
Dispatcher.UIThread.RunJobs();
Check(!shell.IsPaneOpen && content.IsEffectivelyEnabled, "TwoWay light-dismiss state propagation");

Check(first.ContextMenu is not null && second.ContextMenu is not null, "real App TextBox style attaches menus");
var firstMenu = first.ContextMenu!;
var secondMenu = second.ContextMenu!;
Check(!ReferenceEquals(firstMenu, secondMenu), "menus must not share a TextBox target");
var firstItems = firstMenu.ItemsSource!.OfType<MenuItem>().ToArray();
firstMenu.Open(first);
Dispatcher.UIThread.RunJobs();
firstItems[3].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
Check(first.SelectedText == "first" && second.SelectedText == "", "select-all targets the owning TextBox");
firstMenu.Close();
firstMenu.Open(first);
Dispatcher.UIThread.RunJobs();
Check(firstItems[0].IsEnabled && firstItems[1].IsEnabled, "copy/cut state after reopening");
firstItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
Dispatcher.UIThread.RunJobs();
Check(window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() == "first", "copy command uses the owning TextBox");
firstItems[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
Dispatcher.UIThread.RunJobs();
Check(first.Text == "" && second.Text == "second", "cut command modifies only the owning TextBox");
firstItems[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
Dispatcher.UIThread.RunJobs();
Check(first.Text == "first", "paste command restores clipboard text");
first.SelectAll();
firstMenu.Close();
first.IsReadOnly = true;
firstMenu.Open(first);
Dispatcher.UIThread.RunJobs();
Check(firstItems[0].IsEnabled && !firstItems[1].IsEnabled && !firstItems[2].IsEnabled, "read-only menu permissions");
firstMenu.Close();
first.PasswordChar = '*';
firstMenu.Open(first);
Dispatcher.UIThread.RunJobs();
Check(!firstItems[0].IsEnabled && !firstItems[1].IsEnabled, "password contents cannot be copied or cut");
firstMenu.Close();
TextBoxEditMenu.SetEnabled(first, false);
Check(first.ContextMenu is null, "attached menu can be removed");
var custom = new ContextMenu();
first.ContextMenu = custom;
TextBoxEditMenu.SetEnabled(first, true);
Check(ReferenceEquals(first.ContextMenu, custom), "explicit custom menu is preserved");
window.Close();

// Load the real settings XAML without a VM (no global Init/user settings).
// Test actual pointer/pressed template styles in both themes, not copied setters.
var settings = new OneLauncher.Views.settings();
var settingsWindow = new Window { Content = settings, Width = 1100, Height = 800 };
settingsWindow.Show();
Dispatcher.UIThread.RunJobs();
var github = settings.FindControl<Button>("GitHubLink")!;
var githubClicks = 0;
// No real VM/network/browser launch in a regression test. An unresolved Command
// binding disables Button in Avalonia, so supply a deterministic test command.
github.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => githubClicks++);
github.BringIntoView();
Dispatcher.UIThread.RunJobs();
var settingsScroll = settings.GetVisualDescendants().OfType<ScrollViewer>().First();
settingsScroll.Offset = new Vector(0, settingsScroll.Extent.Height);
Dispatcher.UIThread.RunJobs();
var presenter = github.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
var websiteLabel = github.GetVisualParent()!.GetVisualChildren().OfType<TextBlock>().Last();
Check(github.Padding == default && github.MinHeight == 0, "hyperlink has no button padding or minimum height");
var labelCenter = websiteLabel.TranslatePoint(new Point(0, websiteLabel.Bounds.Height / 2), settingsWindow)!.Value.Y;
var linkCenter = github.TranslatePoint(new Point(0, github.Bounds.Height / 2), settingsWindow)!.Value.Y;
Check(Math.Abs(labelCenter - linkCenter) < 1, "website label and hyperlink are aligned");
foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
{
    settingsWindow.RequestedThemeVariant = theme;
    using var initialFrame = settingsWindow.CaptureRenderedFrame();
    settingsWindow.MouseMove(new Point(0, 0));
    Dispatcher.UIThread.RunJobs();
    Check(presenter.Background is ISolidColorBrush normal && normal.Color.A == 0, "transparent normal hyperlink: " + theme);
    var target = github.TranslatePoint(new Point(github.Bounds.Width / 2, github.Bounds.Height / 2), settingsWindow)!.Value;
    settingsWindow.MouseMove(target);
    Dispatcher.UIThread.RunJobs();
    Check(github.IsPointerOver, "pointer reaches settings hyperlink: " + theme);
    Check(presenter.Background is ISolidColorBrush hover && hover.Color.A == 0, "transparent hover hyperlink: " + theme);
    var expected = theme == ThemeVariant.Light ? Color.Parse("#800080") : Color.Parse("#C084FC");
    Check(presenter.Foreground is ISolidColorBrush foreground && foreground.Color == expected, "purple hover hyperlink: " + theme);
    settingsWindow.MouseDown(target, MouseButton.Left);
    Dispatcher.UIThread.RunJobs();
    Check(presenter.Background is ISolidColorBrush pressed && pressed.Color.A == 0, "transparent pressed hyperlink: " + theme);
    settingsWindow.MouseUp(target, MouseButton.Left);
    if (args.Length == 1)
    {
        Directory.CreateDirectory(args[0]);
        using var frame = settingsWindow.CaptureRenderedFrame();
        frame?.Save(Path.Combine(args[0], "settings-hyperlink-" + theme.Key + ".png"));
    }
}
Check(githubClicks == 2, "hyperlink remains clickable in both themes");
settingsWindow.Close();

// Actual manager load/save paths, including nested source-generated metadata and
// IJsonOnDeserialized identity repair; reflection serializer is disabled above.
Task.Run(async () =>
{
    var scratch = Path.Combine(Path.GetTempPath(), "OneLauncher-AotSmoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratch);
    try
    {
        var accounts = new AccountManager(Path.Combine(scratch, "accounts.json"));
        await accounts.InitializeAsync();
        var userId = accounts.Data.DefaultUserID;
        var reloadedAccounts = new AccountManager(Path.Combine(scratch, "accounts.json"));
        await reloadedAccounts.InitializeAsync();
        Check(userId.HasValue && reloadedAccounts.GetDefaultUser().UserID == userId, "account identity roundtrip");

        var configPath = Path.Combine(scratch, "config.json");
        var configs = new DBManager(configPath);
        await configs.InitializeAsync();
        configs.Data.AvailableJavas[21] = "unicode-中文/java";
        configs.Data.OlanSettings.MaximumDownloadThreads = 7;
        await configs.Save();
        var reloadedConfigs = new DBManager(configPath);
        await reloadedConfigs.InitializeAsync();
        Check(reloadedConfigs.Data.AvailableJavas[21] == "unicode-中文/java" &&
              reloadedConfigs.Data.OlanSettings.MaximumDownloadThreads == 7, "nested config roundtrip");

        var gamesPath = Path.Combine(scratch, "games.json");
        var games = new GameDataManager(gamesPath);
        await games.InitializeAsync();
        games.Data.DefaultInstanceMap["1.21"] = "fixture";
        games.Data.Instances["fixture"] = new GameData("中文 instance", "1.21", ModEnum.fabric, userId);
        await games.Save();
        var reloadedGames = new GameDataManager(gamesPath);
        await reloadedGames.InitializeAsync();
        Check(reloadedGames.Data.DefaultInstanceMap["1.21"] == "fixture", "game-data dictionary roundtrip");
        Check(reloadedGames.Data.Instances["fixture"].InstanceId == "fixture" &&
              reloadedGames.Data.Instances["fixture"].ModLoader == ModEnum.fabric, "game-data constructor, enum and identity repair");

        var osName = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var archName = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var otherArch = archName == "x64" ? "arm64" : "x64";
        var librariesJson = """
            {"libraries":[
              {"name":"test:current:1","rules":[{"action":"allow","os":{"name":"$OS","arch":"$ARCH"}}],
               "downloads":{"artifact":{"path":"test/current.jar","url":"https://example.invalid/current.jar","size":1,"sha1":"fixture"}}},
              {"name":"test:other:1","rules":[{"action":"allow","os":{"name":"$OS","arch":"$OTHER"}}],
               "downloads":{"artifact":{"path":"test/other.jar","url":"https://example.invalid/other.jar","size":1,"sha1":"fixture"}}}
            ]}
            """.Replace("$OS", osName).Replace("$ARCH", archName).Replace("$OTHER", otherArch);
        var version = new VersionInfomations(librariesJson, scratch);
        Check(version.GetLibraries().Count == 1, "download rules match target OS and process architecture");
        var classpath = version.GetLibraryiesForUsing();
        Check(classpath.Count == 1 && classpath.ContainsKey("test:current"), "classpath and download architecture rules agree");
    }
    finally
    {
        // Only the unique directory this process just created, never user data.
        Directory.Delete(scratch, recursive: true);
    }
}).GetAwaiter().GetResult();
Console.WriteLine($"PASS: {assertions} UI/JSON assertions (reflection JSON disabled).");
