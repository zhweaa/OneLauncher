using Avalonia.Media;
using SKGT = OneLauncher.Assets.Resources.SKGT;
namespace OneLauncher.Converters;

/// <summary>Shared geometries parsed once from the path strings in SKGT.resx.</summary>
public static class VectorIcons
{
    public static Geometry StatusRunning { get; } = Geometry.Parse(SKGT.StatusRunning);
    public static Geometry StatusCompleted { get; } = Geometry.Parse(SKGT.StatusCompleted);
    public static Geometry StatusCanceled { get; } = Geometry.Parse(SKGT.StatusCanceled);
    public static Geometry StatusFailed { get; } = Geometry.Parse(SKGT.StatusFailed);
    public static Geometry ChevronLeft { get; } = Geometry.Parse(SKGT.ChevronLeft);
    public static Geometry ChevronRight { get; } = Geometry.Parse(SKGT.ChevronRight);
    public static Geometry TaskRunning { get; } = Geometry.Parse(SKGT.TaskRunning);
    public static Geometry TaskStopped { get; } = Geometry.Parse(SKGT.TaskStopped);
    public static Geometry TaskCompleted { get; } = Geometry.Parse(SKGT.TaskCompleted);
}
