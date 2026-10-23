using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OneLauncher.Views;
using OneLauncher.Views.Controls;

// Loads the real application resources without initializing launcher services or user data.
AppBuilder.Configure<OneLauncher.App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
var output = Path.GetFullPath(args.FirstOrDefault() ?? "build/theme-icons-20261023");
Directory.CreateDirectory(output);
var app = Application.Current!;
var keys = Directory.GetFiles("OneLauncher/Assets/icos", "*.png")
    .OrderBy(Path.GetFileName).Select(path => Path.GetFileNameWithoutExtension(path))
    .Select(name => "Icon" + char.ToUpperInvariant(name[0]) + name[1..]).ToArray();
var gallery = new WrapPanel { Width = 650 };
var images = new Dictionary<string, Image>();
foreach (var key in keys)
{
    var image = new Image { Width = 32, Height = 32 };
    image.Bind(Image.SourceProperty, new DynamicResourceExtension(key));
    images.Add(key, image);
    gallery.Children.Add(new StackPanel { Width = 90, Height = 74, Spacing = 6,
        Children = { image, new TextBlock { Text = key[4..], HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center } } });
}
var page = new account { Height = 120 };
var header = new PaneHeader { Title = "Pane header" };
var content = new StackPanel { Margin = new Thickness(20), Spacing = 10,
    Children = { gallery, header, page } };
var window = new Window { Width = 700, Height = 470, Content = content };
window.Show();
foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light })
{
    window.RequestedThemeVariant = theme;
    window.Background = theme == ThemeVariant.Light ? Brushes.White : new SolidColorBrush(Color.Parse("#1D1E21"));
    for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
    foreach (var (key, image) in images)
    {
        Assert(app.TryGetResource(key, theme, out var value) && value is Bitmap, $"Missing bitmap: {theme}/{key}");
        Assert(ReferenceEquals(image.Source, value), $"Dynamic resource did not switch: {theme}/{key}");
        Assert(app.TryGetResource(key, theme == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light, out var other)
            && !ReferenceEquals(value, other), $"Themes use the same bitmap: {key}");
    }
    var back = header.GetVisualDescendants().OfType<Image>().Single();
    Assert(app.TryGetResource("IconBack", theme, out var backResource) && ReferenceEquals(back.Source, backResource),
        $"Template icon did not switch: {theme}");
    var toolbarImages = page.GetVisualDescendants().OfType<Image>().ToArray();
    Assert(toolbarImages.Length == 2 && toolbarImages.All(image => image.Source is Bitmap), $"Page icons missing: {theme}");
    foreach (var key in new[] { "IconAdd", "IconRefresh" })
        Assert(app.TryGetResource(key, theme, out var resource) && toolbarImages.Any(image => ReferenceEquals(image.Source, resource)),
            $"Page icon did not switch: {theme}/{key}");
    using var frame = window.CaptureRenderedFrame();
    Assert(frame != null, "Window frame was not rendered");
    frame!.Save(Path.Combine(output, $"icons-{theme.ToString().ToLowerInvariant()}.png"));
}
app.RequestedThemeVariant = ThemeVariant.Dark;
window.RequestedThemeVariant = ThemeVariant.Default;
Dispatcher.UIThread.RunJobs();
Assert(window.ActualThemeVariant == ThemeVariant.Dark && app.TryGetResource("IconHome", ThemeVariant.Dark, out var inherited)
    && ReferenceEquals(images["IconHome"].Source, inherited), "Default window theme must inherit the application theme");
window.Close();
Console.WriteLine($"PASS: {keys.Length} bitmap pairs, live Light/Dark/Light switch, template/page icons, application theme inheritance, rendered screenshots");

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
