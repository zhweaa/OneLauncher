using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Server;
using OneLauncher.Views;
using OneLauncher.Views.Controls;

internal static class MagicHomeUiChecks
{
    public static async Task RunAsync(string output)
    {
        var root = Path.Combine(output, "data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // 背景副本目录指向测试数据目录，绝不读写用户真实的 customdata。
        typeof(MainWindow).Assembly.GetType("OneLauncher.Codes.HomeBackgroundStore", true)!
            .GetProperty("CustomDataPath", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.Combine(root, "customdata"));
        var config = new DBManager(Path.Combine(root, "config.json"));
        await config.InitializeAsync();
        var games = new GameDataManager(Path.Combine(root, "games.json"));
        await games.InitializeAsync();
        Init.GameRootPath = Path.Combine(root, "minecraft");
        var game = new GameData("1.20.6-fabric-0.16.14", "1.20.6", ModEnum.fabric, Guid.Empty,
            DateTime.Now, null, null, null) { InstanceId = "magic001" };
        games.Data.Instances[game.InstanceId] = game;
        var server = new ServerEntry(Guid.NewGuid(), game.InstanceId,
            new ServerInfo { Ip = "mc.example.net", Port = 25565 }, "生存服务器", null);
        config.Data.ServerList.Add(server);
        config.Data.OlanSettings.DefaultInstanceID = game.InstanceId;
        await config.Save();

        var core = (UnitedCore)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
        typeof(UnitedCore).GetProperty(nameof(UnitedCore.GameDataManager))!.SetValue(core, games);
        var assembly = typeof(MainWindow).Assembly;
        var homeType = assembly.GetType("OneLauncher.Views.ViewModels.HomePageViewModel", true)!;
        var home = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(homeType);
        // 主页构造函数会启动新闻、更新和联机初始化；这里只初始化离线视图所需字段。
        SetField(homeType, home, "_configManager", config);
        SetField(homeType, home, "_unitedCore", core);
        SetField(homeType, home, "isNewsEnabled", true);
        SetField(homeType, home, "isTaskCenterEnabled", true);
        SetField(homeType, home, "isPowerPlayEnabled", true);
        SetField(homeType, home, "isImportEnabled", true);
        homeType.GetProperty("CurrentNews")!.SetValue(home, new MinecraftNew(
            "Minecraft Live", "新的方块、生物和更新正在路上。", "", ""));
        var apply = homeType.GetMethod("ApplyHomeSettings", BindingFlags.NonPublic | BindingFlags.Instance)!;
        apply.Invoke(home, null);
        config.OnDataChanged += () => apply.Invoke(home, null);
        var launchItems = ((System.Collections.IEnumerable)homeType.GetProperty("Items")!.GetValue(home)!).Cast<object>().ToArray();
        Assert(launchItems.Length == 2 && launchItems.Any(i => Name(i) == game.Name) && launchItems.Any(i => Name(i) == server.Name),
            "Quick launch list must contain game data and favorite servers");
        Assert(Name(homeType.GetProperty("SelectedLaunchItem")!.GetValue(home)) == game.Name,
            "Default instance must be selected initially");

        var page = new OneLauncher.Home { DataContext = home };
        var window = new Window { Width = 818, Height = 530, Content = page,
            Background = Brushes.White, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Settle();
        var classicLaunchPanel = page.GetVisualDescendants().OfType<Panel>().Single(p => p.Width == 300);
        Assert(classicLaunchPanel.IsVisible, "Default style must keep the right quick launch panel");
        var cardBorders = page.GetVisualDescendants().OfType<ListBoxItem>()
            .Select(i => i.GetVisualDescendants().OfType<Border>().First(b => b.Width == 385)).ToArray();
        Assert(cardBorders.Length == 4 && cardBorders.All(b => b.BoxShadow.Count > 0 && b.BoxShadow[0].Blur > 0),
            "Home section cards must use the card-list shadow");
        var imagePath = Path.Combine(root, "background.png");
        CreateBackground(imagePath);
        config.Data.OlanSettings.HomeBackgroundImagePath = imagePath;
        await config.Save();
        Settle();
        Assert(!(bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)!, "Default style must not show background");
        Save(window, output, "home-classic.png");

        var settingsType = assembly.GetType("OneLauncher.Views.ViewModels.SettingsPageViewModel", true)!;
        var settingsVm = Activator.CreateInstance(settingsType, config, core)!;
        var settingsItems = ((System.Collections.IEnumerable)settingsType.GetProperty("HomeLaunchItems")!.GetValue(settingsVm)!).Cast<object>().ToArray();
        settingsType.GetProperty("SelectedHomeLaunchItem")!.SetValue(settingsVm, settingsItems.Single(i => Name(i) == server.Name));
        await WaitUntil(() => config.Data.OlanSettings.HomeDefaultServerID == server.Id);
        Settle();
        Assert((string)homeType.GetProperty("MagicLaunchTitle")!.GetValue(home)! == server.Name &&
            Name(homeType.GetProperty("SelectedLaunchItem")!.GetValue(home)) == server.Name,
            "Settings default launch item must drive the home page");
        settingsType.GetProperty("SelectedHomePageStyle")!.SetValue(settingsVm, "魔法按钮");
        await WaitUntil(() => config.Data.OlanSettings.HomePageStyle == HomePageStyle.MagicButton);
        Settle();
        Assert(!classicLaunchPanel.IsVisible, "Magic style must replace the right quick launch panel");
        var button = page.FindControl<MagicLaunchButton>("MagicLaunchButton")!;
        Assert(button.IsEffectivelyVisible, "Magic launch button must be visible");
        Assert(!page.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible),
            "Magic style must not offer launch item selection");
        Assert((bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)!, "Background must load in magic style");
        var buttonTexts = button.GetVisualDescendants().OfType<TextBlock>().ToArray();
        var title = buttonTexts.Single(t => t.Classes.Contains("magic-launch-title"));
        var titleCenter = title.TranslatePoint(new Point(title.Bounds.Width / 2, 0), button)!.Value.X;
        Assert(buttonTexts.Length == 1 && title.Text == server.Name && Math.Abs(titleCenter - button.Bounds.Width / 2) < 2,
            $"Magic button must only show the centered launch item name: {buttonTexts.Length}, {titleCenter}/{button.Bounds.Width}");
        var sectionsScroll = page.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Classes.Contains("home-sections"));
        Assert(sectionsScroll.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            "Home section scroll bar must be hidden");
        var imageCards = page.GetVisualDescendants().OfType<ListBoxItem>()
            .Select(i => i.GetVisualDescendants().OfType<Border>().First(b => b.Width == 385)).ToArray();
        Assert(imageCards.All(b => b.Background is ISolidColorBrush { Color.A: < 255 and > 128 }),
            "Cards on a custom background must use a translucent base color");
        Save(window, output, "home-magic-background.png");

        config.Data.OlanSettings.IsHomePowerPlayVisible = false;
        config.Data.OlanSettings.IsHomeImportVisible = false;
        await config.Save();
        Settle();
        Save(window, output, "home-magic-two-sections.png");

        var surface = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Surface");
        ((IPseudoClasses)button.Classes).Set(":pointerover", true);
        Settle();
        Assert(surface.BoxShadow.Count == 2 && surface.BoxShadow[0].IsInset && surface.BoxShadow[0].Spread > 0 &&
            surface.BoxShadow[1].Blur > 0 && surface.Background is ISolidColorBrush { Color.A: >= 0x80 },
            "Hover on a custom background must add an outline, darker base and outer shadow");
        Save(window, output, "home-magic-hover.png");
        button.StartBurst(new Point(button.Bounds.Width * 0.3, button.Bounds.Height * 0.6));
        var startDiameter = button.BurstShadowDiameter;
        var startOpacity = button.BurstShadowOpacity;
        RunFrames(90);
        var midDiameter = button.BurstShadowDiameter;
        var midOpacity = button.BurstShadowOpacity;
        Save(window, output, "home-magic-burst-early.png");
        RunFrames(200);
        Assert(button.BurstShadowDiameter > midDiameter && midDiameter > startDiameter &&
            button.BurstShadowOpacity > midOpacity && midOpacity > startOpacity,
            $"Burst shadow must spread and deepen: {startDiameter}/{startOpacity}, {midDiameter}/{midOpacity}, " +
            $"{button.BurstShadowDiameter}/{button.BurstShadowOpacity}");
        Save(window, output, "home-magic-burst-late.png");
        RunFrames(700);
        Assert(button.GetVisualDescendants().OfType<Canvas>().Single().Children.Count == 0, "Burst shadow must clean up");
        ((IPseudoClasses)button.Classes).Set(":pointerover", false);

        config.Data.OlanSettings.IsHomeNewsVisible = false;
        config.Data.OlanSettings.IsHomeTaskCenterVisible = false;
        await config.Save();
        Settle();
        Assert(!(bool)homeType.GetProperty("IsHomeSectionsVisible")!.GetValue(home)!, "All hidden sections must collapse the left area");
        Save(window, output, "home-magic-all-hidden.png");

        window.Width = 700;
        window.Height = 470;
        window.RequestedThemeVariant = ThemeVariant.Dark;
        window.Background = new SolidColorBrush(Color.Parse("#1D1E21"));
        config.Data.OlanSettings.IsHomeNewsVisible = true;
        config.Data.OlanSettings.IsHomeTaskCenterVisible = true;
        config.Data.OlanSettings.HomeBackgroundImagePath = null;
        await config.Save();
        Settle();
        Save(window, output, "home-magic-dark-small.png");

        settingsType.GetProperty("SelectedHomePageStyle")!.SetValue(settingsVm, "默认");
        await WaitUntil(() => config.Data.OlanSettings.HomePageStyle == HomePageStyle.Classic);
        Settle();
        var sectionItems = page.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("card-list"))
            .Items.OfType<ListBoxItem>().ToArray();
        Assert(classicLaunchPanel.IsVisible && sectionItems.Length == 4 && sectionItems.All(i => i.IsVisible),
            $"Default style must ignore magic section visibility switches: {classicLaunchPanel.IsVisible}, " +
            string.Join(",", sectionItems.Select(i => i.IsVisible)));
        Save(window, output, "home-classic-dark-server.png");
        window.Close();

        await CheckSettingsAsync(output, config, core, settingsType);
        await CheckBackgroundStoreAsync(root, imagePath);
        await CheckStartupClassicThenMagicAsync(output, root, core, imagePath);
        Console.WriteLine("PASS: magic home style, quick launch list, section visibility, background, hover and burst");
    }

    // 复现：配置被旧版本启动器重写（样式回到默认、背景字段丢失），但 customdata 中的背景副本仍在；
    // 之后在设置页（主页不在窗口中）切换为魔法按钮，应自动找回该背景。
    private static async Task CheckStartupClassicThenMagicAsync(string output, string root, UnitedCore core, string imagePath)
    {
        var store = typeof(MainWindow).Assembly.GetType("OneLauncher.Codes.HomeBackgroundStore", true)!;
        var copied = await (Task<string>)store.GetMethod("ImportAsync")!.Invoke(null, [imagePath])!;
        var configRoot = Path.Combine(root, "startup-classic");
        Directory.CreateDirectory(configRoot);
        var config = new DBManager(Path.Combine(configRoot, "config.json"));
        await config.InitializeAsync();
        config.Data.OlanSettings.HomePageStyle = HomePageStyle.Classic;
        config.Data.OlanSettings.HomeBackgroundImagePath = null;
        await config.Save();

        var assembly = typeof(MainWindow).Assembly;
        var homeType = assembly.GetType("OneLauncher.Views.ViewModels.HomePageViewModel", true)!;
        var home = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(homeType);
        SetField(homeType, home, "_configManager", config);
        SetField(homeType, home, "_unitedCore", core);
        homeType.GetProperty("CurrentNews")!.SetValue(home, new MinecraftNew("Minecraft Live", "", "", ""));
        var apply = homeType.GetMethod("ApplyHomeSettings", BindingFlags.NonPublic | BindingFlags.Instance)!;
        apply.Invoke(home, null);
        config.OnDataChanged += () => apply.Invoke(home, null);

        var page = new OneLauncher.Home { DataContext = home };
        var host = new ContentControl { Content = page };
        var window = new Window { Width = 818, Height = 530, Content = host,
            Background = Brushes.White, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Settle();
        Assert(!(bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)!, "Classic startup must not show background");

        var settingsType = assembly.GetType("OneLauncher.Views.ViewModels.SettingsPageViewModel", true)!;
        var settingsVm = Activator.CreateInstance(settingsType, config, core)!;
        host.Content = new settings { DataContext = settingsVm };
        Settle();
        settingsType.GetProperty("SelectedHomePageStyle")!.SetValue(settingsVm, "魔法按钮");
        await WaitUntil(() => config.Data.OlanSettings.HomePageStyle == HomePageStyle.MagicButton);
        Settle();
        host.Content = page;
        Settle();
        var image = page.GetVisualDescendants().OfType<Image>().First();
        Assert(config.Data.OlanSettings.HomeBackgroundImagePath == copied &&
            (bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)! && image.IsEffectivelyVisible &&
            image.Source != null && image.Bounds.Width > 0,
            $"Switching to magic style must apply the saved background: {homeType.GetProperty("HasHomeBackground")!.GetValue(home)}, " +
            $"{image.IsEffectivelyVisible}, {image.Source != null}, {image.Bounds}");
        Save(window, output, "home-startup-classic-then-magic.png");

        // 移除背景会清空所有副本，再次切换样式不能把已移除的背景找回来。
        await ((IAsyncRelayCommand)settingsType.GetProperty("ClearHomeBackgroundCommand")!.GetValue(settingsVm)!).ExecuteAsync(null);
        Settle();
        settingsType.GetProperty("SelectedHomePageStyle")!.SetValue(settingsVm, "默认");
        await WaitUntil(() => config.Data.OlanSettings.HomePageStyle == HomePageStyle.Classic);
        settingsType.GetProperty("SelectedHomePageStyle")!.SetValue(settingsVm, "魔法按钮");
        await WaitUntil(() => config.Data.OlanSettings.HomePageStyle == HomePageStyle.MagicButton);
        Settle();
        Assert(config.Data.OlanSettings.HomeBackgroundImagePath == null && !File.Exists(copied) &&
            !(bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)!,
            "A removed background must stay removed after switching styles");
        window.Close();
    }

    private static async Task CheckBackgroundStoreAsync(string root, string sourcePath)
    {
        var store = typeof(MainWindow).Assembly.GetType("OneLauncher.Codes.HomeBackgroundStore", true)!;
        var customData = Path.Combine(root, "customdata");
        var import = store.GetMethod("ImportAsync")!;
        var copied = await (Task<string>)import.Invoke(null, [sourcePath])!;
        var expectedRoot = Path.Combine(customData, "home-background") + Path.DirectorySeparatorChar;
        Assert(copied.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(copied) &&
            File.ReadAllBytes(copied).SequenceEqual(File.ReadAllBytes(sourcePath)),
            $"Background must be copied into customdata: {copied}");
        Assert((string?)store.GetMethod("FindLatest")!.Invoke(null, null) == copied, "Latest background copy must be discoverable");
        store.GetMethod("TryDelete")!.Invoke(null, [sourcePath]);
        Assert(File.Exists(sourcePath), "Deleting a background must never remove the user's original file");
        store.GetMethod("TryDelete")!.Invoke(null, [copied]);
        Assert(!File.Exists(copied) && !Directory.Exists(Path.GetDirectoryName(copied)) && Directory.Exists(expectedRoot),
            "Replaced background copy must be removed while keeping the store root");
    }

    private static string? Name(object? item) => item?.GetType().GetProperty("Name")?.GetValue(item) as string;

    private static async Task CheckSettingsAsync(string output, DBManager config, UnitedCore core, Type settingsType)
    {
        config.Data.OlanSettings.IsHomePowerPlayVisible = true;
        await config.Save();
        var vm = Activator.CreateInstance(settingsType, config, core)!;
        var page = new settings { DataContext = vm };
        var window = new Window { Width = 698, Height = 640, Content = page,
            Background = Brushes.White, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        page.FindControl<ComboBox>("SettingsCategoryComboBox")!.SelectedIndex = 1;
        page.FindControl<ComboBox>("HomePageStyleComboBox")!.SelectedItem = "魔法按钮";
        await WaitUntil(() => config.Data.OlanSettings.HomePageStyle == HomePageStyle.MagicButton);
        Settle();
        var button = page.FindControl<Button>("HomeSectionVisibilityButton")!;
        Save(window, output, "settings-interface.png");
        var flyout = (SettingsFlyout)button.Flyout!;
        flyout.ShowAt(button);
        Settle();
        var boxes = ((Grid)flyout.Content!).GetVisualDescendants().OfType<CheckBox>().ToArray();
        Assert(boxes.Select(b => b.Content as string).SequenceEqual(["MC新闻", "任务中心", "联机面板", "导入游戏实例"]),
            "Home section flyout must expose the four requested sections");
        boxes.Single(b => Equals(b.Content, "联机面板")).IsChecked = false;
        Assert(config.Data.OlanSettings.IsHomePowerPlayVisible, "Checkbox editing must not write config before saving");
        Save(window, output, "settings-home-sections.png");
        await ((IAsyncRelayCommand)settingsType.GetProperty("SaveHomeSectionsCommand")!.GetValue(vm)!).ExecuteAsync(null);
        Settle();
        var reload = new DBManager(Path.Combine(Path.GetDirectoryName(GetConfigPath(config))!, "config.json"));
        await reload.InitializeAsync();
        Assert(!reload.Data.OlanSettings.IsHomePowerPlayVisible && !flyout.IsOpen, "Saved section visibility must persist and close");
        window.Close();
    }

    private static string GetConfigPath(DBManager config)
        => (string)typeof(BasicDataManager<AppConfig>).GetProperty("ConfigPath", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(config)!;

    private static void CreateBackground(string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 720));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#FF2B7A4B"), 0), new GradientStop(Color.Parse("#FF1E88E5"), 0.55), new GradientStop(Color.Parse("#FFFFB300"), 1) }
            }, new Rect(0, 0, 1280, 720));
            var pen = new Pen(new SolidColorBrush(Color.Parse("#55FFFFFF")), 18);
            for (var x = -720; x < 1280; x += 96)
                context.DrawLine(pen, new Point(x, 720), new Point(x + 720, 0));
        }
        bitmap.Save(path);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert(condition(), "Timed out waiting for settings save");
    }

    private static void Settle() => RunFrames(350);

    private static void RunFrames(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(8);
        }
    }

    private static void Save(Window window, string output, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(output, name));
    }

    private static void SetField(Type type, object target, string name, object value)
        => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
