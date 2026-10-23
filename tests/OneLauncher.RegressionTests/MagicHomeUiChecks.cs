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
        var store = typeof(MainWindow).Assembly.GetType("OneLauncher.Codes.HomeBackgroundStore", true)!;
        store.GetProperty("CustomDataPath", BindingFlags.NonPublic | BindingFlags.Static)!
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
        var refreshBackground = (Action)Delegate.CreateDelegate(typeof(Action), home,
            homeType.GetMethod("RefreshHomeBackground", BindingFlags.NonPublic | BindingFlags.Instance)!);
        var changedEvent = store.GetEvent("Changed")!;
        changedEvent.AddEventHandler(null, refreshBackground);
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
        var imported = await (Task<string>)store.GetMethod("ImportAsync")!.Invoke(null, [imagePath])!;
        Settle();
        Assert(!Path.GetFullPath(imported).Equals(Path.GetFullPath(imagePath), StringComparison.OrdinalIgnoreCase),
            "Background must be copied into customdata");
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
        Assert(surface.BoxShadow.Count == 0 && surface.BorderThickness == default,
            "Magic button must not draw an outline or frame shadow");
        var newsCard = page.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("home-news-card"));
        Assert(newsCard.BorderThickness == default, "News card must not draw an outline");
        var titleBlock = button.GetVisualDescendants().OfType<InkCenteredTextBlock>().Single();
        var layout = titleBlock.TextLayout;
        var inkTop = layout.Height + layout.OverhangAfter - layout.Extent + titleBlock.InkOffsetY;
        var inkBottom = layout.Height + layout.OverhangAfter + titleBlock.InkOffsetY;
        Assert(Math.Abs(inkTop - (titleBlock.Bounds.Height - inkBottom)) < 1.5 && titleBlock.InkOffsetY < 0,
            $"Magic button title strokes must be vertically centered: {inkTop}/{titleBlock.Bounds.Height - inkBottom}");

        // 用真实的鼠标移动触发悬停：阴影中心跟随指针，远离指针处与背景完全一致。
        var buttonOrigin = button.TranslatePoint(default, window)!.Value;
        var before = Pixel(window, buttonOrigin + new Point(button.Bounds.Width - 6, 6));
        var hoverPoint = new Point(button.Bounds.Width * 0.25, button.Bounds.Height * 0.5);
        window.MouseMove(buttonOrigin + hoverPoint);
        Settle();
        var glowCenter = button.HoverGlowCenter;
        Assert(button.IsPointerOver && glowCenter is { } c && Math.Abs(c.X - hoverPoint.X) < 2 && Math.Abs(c.Y - hoverPoint.Y) < 2,
            $"Hover shadow must center on the pointer: {glowCenter}/{hoverPoint}");
        var farAfter = Pixel(window, buttonOrigin + new Point(button.Bounds.Width - 6, 6));
        Assert(Math.Abs(farAfter.R - before.R) + Math.Abs(farAfter.G - before.G) + Math.Abs(farAfter.B - before.B) <= 6,
            $"Hover shadow must fade into the background far from the pointer: {before}/{farAfter}");
        Save(window, output, "home-magic-hover.png");
        window.MouseMove(buttonOrigin + new Point(button.Bounds.Width * 0.75, button.Bounds.Height * 0.5));
        Settle();
        Assert(button.HoverGlowCenter is { X: > 0 } moved && moved.X > hoverPoint.X + 20, "Hover shadow must follow the pointer");
        Save(window, output, "home-magic-hover-moved.png");
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
        Assert(button.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "PART_BurstLayer").Children.Count == 0,
            "Burst shadow must clean up");
        window.MouseMove(new Point(2, 2));
        Settle();
        Assert(button.HoverGlowCenter == null, "Hover shadow must hide when the pointer leaves");

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
        await config.Save();
        store.GetMethod("Clear")!.Invoke(null, null);
        Settle();
        Assert(!(bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)!, "Clearing the directory must hide the background");
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
        changedEvent.RemoveEventHandler(null, refreshBackground);

        await CheckSettingsAsync(output, config, core, settingsType);
        await CheckBackgroundStoreAsync(root, imagePath);
        await CheckStartupClassicThenMagicAsync(output, root, core, imagePath);
        Console.WriteLine("PASS: magic home style, quick launch list, section visibility, background, hover and burst");
    }

    // 配置以默认样式启动但 customdata 中已有背景文件；在设置页（主页不在窗口中）切换为魔法按钮后应直接显示。
    private static async Task CheckStartupClassicThenMagicAsync(string output, string root, UnitedCore core, string imagePath)
    {
        var store = typeof(MainWindow).Assembly.GetType("OneLauncher.Codes.HomeBackgroundStore", true)!;
        var copied = await (Task<string>)store.GetMethod("ImportAsync")!.Invoke(null, [imagePath])!;
        var configRoot = Path.Combine(root, "startup-classic");
        Directory.CreateDirectory(configRoot);
        var config = new DBManager(Path.Combine(configRoot, "config.json"));
        await config.InitializeAsync();
        config.Data.OlanSettings.HomePageStyle = HomePageStyle.Classic;
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
        Assert((string?)settingsType.GetProperty("CurrentHomeBackgroundPath")!.GetValue(settingsVm) == copied &&
            (bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)! && image.IsEffectivelyVisible &&
            image.Source != null && image.Bounds.Width > 0,
            $"Switching to magic style must apply the saved background: {homeType.GetProperty("HasHomeBackground")!.GetValue(home)}, " +
            $"{image.IsEffectivelyVisible}, {image.Source != null}, {image.Bounds}");
        Save(window, output, "home-startup-classic-then-magic.png");

        // 移除背景会清空目录，再次切换样式也不会显示背景。
        var refreshBackground = (Action)Delegate.CreateDelegate(typeof(Action), home,
            homeType.GetMethod("RefreshHomeBackground", BindingFlags.NonPublic | BindingFlags.Instance)!);
        store.GetEvent("Changed")!.AddEventHandler(null, refreshBackground);
        ((System.Windows.Input.ICommand)settingsType.GetProperty("ClearHomeBackgroundCommand")!.GetValue(settingsVm)!).Execute(null);
        Settle();
        Assert(!(bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)!, "Removing the background must hide it immediately");
        settingsType.GetProperty("SelectedHomePageStyle")!.SetValue(settingsVm, "默认");
        await WaitUntil(() => config.Data.OlanSettings.HomePageStyle == HomePageStyle.Classic);
        settingsType.GetProperty("SelectedHomePageStyle")!.SetValue(settingsVm, "魔法按钮");
        await WaitUntil(() => config.Data.OlanSettings.HomePageStyle == HomePageStyle.MagicButton);
        Settle();
        Assert(!File.Exists(copied) && settingsType.GetProperty("CurrentHomeBackgroundPath")!.GetValue(settingsVm) == null &&
            !(bool)homeType.GetProperty("HasHomeBackground")!.GetValue(home)!,
            "A removed background must stay removed after switching styles");
        store.GetEvent("Changed")!.RemoveEventHandler(null, refreshBackground);
        window.Close();
    }

    private static async Task CheckBackgroundStoreAsync(string root, string sourcePath)
    {
        var store = typeof(MainWindow).Assembly.GetType("OneLauncher.Codes.HomeBackgroundStore", true)!;
        var customData = Path.Combine(root, "customdata");
        var import = store.GetMethod("ImportAsync")!;
        var first = await (Task<string>)import.Invoke(null, [sourcePath])!;
        var copied = await (Task<string>)import.Invoke(null, [sourcePath])!;
        var expectedRoot = Path.Combine(customData, "home-background") + Path.DirectorySeparatorChar;
        Assert(copied.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(copied) &&
            File.ReadAllBytes(copied).SequenceEqual(File.ReadAllBytes(sourcePath)),
            $"Background must be copied into customdata: {copied}");
        Assert(!File.Exists(first) && (string?)store.GetMethod("FindCurrent")!.Invoke(null, null) == copied,
            "Replacing a background must remove the previous copy and use the new one");

        var invalid = Path.Combine(root, "broken.png");
        File.WriteAllText(invalid, "not an image");
        var rejected = false;
        try { await (Task<string>)import.Invoke(null, [invalid])!; }
        catch (OlanException) { rejected = true; }
        Assert(rejected && (string?)store.GetMethod("FindCurrent")!.Invoke(null, null) == copied,
            "Invalid images must be rejected without replacing the current background");

        store.GetMethod("Clear")!.Invoke(null, null);
        Assert(File.Exists(sourcePath) && !File.Exists(copied) && Directory.Exists(expectedRoot) &&
            store.GetMethod("FindCurrent")!.Invoke(null, null) == null,
            "Clearing must remove copies only, never the user's original file or the store root");
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

    private static Color Pixel(Window window, Point point)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var scale = frame.PixelSize.Width / window.Bounds.Width;
        var x = (int)Math.Round(point.X * scale);
        var y = (int)Math.Round(point.Y * scale);
        var buffer = new byte[4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), buffer.Length, 4);
        }
        finally
        {
            handle.Free();
        }
        return frame.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromArgb(buffer[3], buffer[0], buffer[1], buffer[2])
            : Color.FromArgb(buffer[3], buffer[2], buffer[1], buffer[0]);
    }

    private static void SetField(Type type, object target, string name, object value)
        => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
