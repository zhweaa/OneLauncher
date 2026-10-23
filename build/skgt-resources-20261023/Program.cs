using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OneLauncher.Assets.Resources;
using OneLauncher.Core.Global;

// Offline validation: actual compiled App and views, no launcher initialization or user data.
AppBuilder.Configure<OneLauncher.App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
var output = Path.GetFullPath(args.FirstOrDefault() ?? "build/skgt-resources-20261023");
Directory.CreateDirectory(output);
var baseline = JsonSerializer.Deserialize<Baseline[]>(File.ReadAllText(Path.Combine(output, "baseline.json")))!;
var assembly = typeof(VectorIcons).Assembly;
Assert(assembly.GetManifestResourceNames().Contains("OneLauncher.Assets.Resources.SKGT.resources"),
    "SKGT must be a .NET embedded resource with the correct case-sensitive name");
var manager = new ResourceManager("OneLauncher.Assets.Resources.SKGT", assembly);
var source = XDocument.Load("OneLauncher/Assets/Resources/SKGT.resx").Root!.Elements("data")
    .ToDictionary(node => (string)node.Attribute("name")!, node => node.Element("value")!.Value);
Assert(manager.GetString("TEST", CultureInfo.InvariantCulture) == "TEST1", "User's existing resource was not preserved");
var geometries = new Dictionary<string, Geometry>();
foreach (var entry in baseline)
{
    var resource = manager.GetString(entry.Key, CultureInfo.InvariantCulture)!;
    Assert(resource == source[entry.Key], $"Embedded resource differs from resx: {entry.Key}");
    Assert(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource))) == entry.Hash,
        $"Original path text changed during migration: {entry.Key}");
    foreach (var culture in new[] { "zh-CN", "en-US" })
        Assert(manager.GetString(entry.Key, CultureInfo.GetCultureInfo(culture)) == resource, "Neutral resource fallback failed");
    var property = typeof(VectorIcons).GetProperty(entry.Key, BindingFlags.Public | BindingFlags.Static)!;
    var geometry = (Geometry)property.GetValue(null)!;
    Assert(ReferenceEquals(geometry, property.GetValue(null)), $"Geometry is not cached: {entry.Key}");
    Assert(geometry.Bounds == Geometry.Parse(resource).Bounds && geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0,
        $"Geometry is empty or changed: {entry.Key}");
    geometries.Add(entry.Key, geometry);
}

foreach (var (name, field) in new[] {
    ("DownloadPaneViewModel", "downloadStatus"), ("InstallModPaneViewModel", "installationStatus"),
    ("InstallPackPaneViewModel", "installationStatus") })
{
    var type = assembly.GetType("OneLauncher.Views.Panes.PaneViewModels." + name, true)!;
    var vm = RuntimeHelpers.GetUninitializedObject(type);
    foreach (var (status, key) in new[] {
        (TaskInfoStatus.Running, "StatusRunning"), (TaskInfoStatus.Finished, "StatusCompleted"),
        (TaskInfoStatus.Canceled, "StatusCanceled"), (TaskInfoStatus.Failed, "StatusFailed") })
    {
        type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, status);
        Assert(ReferenceEquals(type.GetProperty("StatusIcon")!.GetValue(vm), geometries[key]),
            $"Incorrect shared icon for {name}/{status}");
    }
}

var gallery = new WrapPanel { Width = 660 };
foreach (var (key, geometry) in geometries)
    gallery.Children.Add(new StackPanel { Width = 210, Height = 64, Spacing = 6,
        Children = { new PathIcon { Data = geometry, Width = 24, Height = 24 },
            new TextBlock { Text = key, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center } } });
var window = new Window { Width = 700, Height = 900, Content = gallery };
window.Show();
foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
{
    window.RequestedThemeVariant = theme;
    window.Background = theme == ThemeVariant.Light ? Brushes.White : new SolidColorBrush(Color.Parse("#1D1E21"));
    Settle();
    Capture($"vectors-{theme.ToString().ToLowerInvariant()}.png");
}

var home = new OneLauncher.Home();
window.Content = home;
Settle();
var homeIcons = home.GetLogicalDescendants().OfType<PathIcon>().ToArray();
Assert(homeIcons.Length == 2 && homeIcons.Any(icon => ReferenceEquals(icon.Data, VectorIcons.ChevronLeft))
    && homeIcons.Any(icon => ReferenceEquals(icon.Data, VectorIcons.ChevronRight)), "Actual Home AXAML did not resolve vector resources");

var tasks = new TaskManager();
tasks.CreateTask("Running");
tasks.CreateTask("Canceled").Cancel();
tasks.CreateTask("Failed").SetFailed(new InvalidOperationException("Offline validation"));
tasks.CreateTask("Finished").SetDone();
var core = (UnitedCore)RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
typeof(UnitedCore).GetProperty(nameof(UnitedCore.TaskManager))!.SetValue(core, tasks);
var taskVmType = assembly.GetType("OneLauncher.Views.Panes.PaneViewModels.TaskCenterPaneViewModel", true)!;
var taskVm = Activator.CreateInstance(taskVmType, core, (Action)(() => { }))!;
var taskViewType = assembly.GetType("OneLauncher.Views.Panes.TaskCenterPane", true)!;
var pane = (Control)Activator.CreateInstance(taskViewType, nonPublic: true)!;
pane.DataContext = taskVm;
window.Content = pane;
foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
{
    window.RequestedThemeVariant = theme;
    window.Background = theme == ThemeVariant.Light ? Brushes.White : new SolidColorBrush(Color.Parse("#1D1E21"));
    Settle();
    var icons = pane.GetVisualDescendants().OfType<PathIcon>().ToArray();
    Assert(icons.Length == 16, "Task center must realize all four task cards");
    Assert(icons.Where(icon => icon.Classes.Contains("running")).All(icon => ReferenceEquals(icon.Data, VectorIcons.TaskRunning)),
        "Running task geometry did not resolve");
    Assert(icons.Where(icon => icon.Classes.Contains("canceled") || icon.Classes.Contains("failed"))
        .All(icon => ReferenceEquals(icon.Data, VectorIcons.TaskStopped)), "Stopped task geometry did not resolve");
    Assert(icons.Where(icon => icon.Classes.Contains("finished")).All(icon => ReferenceEquals(icon.Data, VectorIcons.TaskCompleted)),
        "Finished task geometry did not resolve");
    Assert(icons.Count(icon => icon.IsVisible) == 4, "Each task card must display one status icon");
    Capture($"tasks-{theme.ToString().ToLowerInvariant()}.png");
}
((IDisposable)taskVm).Dispose();
window.Close();
Console.WriteLine("PASS: 9 original paths unchanged, embedded resource/culture lookup, cached geometries, 12 ViewModel states, Home/TaskCenter AXAML and Light/Dark renders");

void Settle()
{
    for (var i = 0; i < 6; i++) Dispatcher.UIThread.RunJobs();
}
void Capture(string filename)
{
    using var frame = window.CaptureRenderedFrame();
    Assert(frame != null, "Rendered frame missing");
    frame!.Save(Path.Combine(output, filename));
}
void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
record Baseline(string Key, string Hash);
