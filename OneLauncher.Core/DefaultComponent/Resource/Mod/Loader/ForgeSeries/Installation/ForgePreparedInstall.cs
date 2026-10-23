using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

internal enum ForgeInstallProtocol { Legacy, Profile }

/// <summary>一次 Provider 操作持有的输入快照；准备阶段不发布版本配置或生成产物。</summary>
internal sealed class ForgePreparedInstall
{
    public required byte[] Installer { get; init; }
    public required string VersionJson { get; init; }
    public required ForgeSeriesVersionJson Version { get; init; }
    public required ForgeSeriesInstallProfile Profile { get; init; }
    public required string Minecraft { get; init; }
    public required string ModType { get; init; }
    public ForgeInstallProtocol Protocol { get; init; }
    public List<ForgeLibraryFile> Libraries { get; } = [];
    public List<ForgeSeriesProcessor> Processors => Profile.Processors
        .Where(p => p.Sides == null || p.Sides.Count == 0 || p.Sides.Contains("client")).ToList();
    public List<NdDowItem> Downloads => Libraries.Where(l => l.EmbeddedEntry == null && !l.Generated).Select(l => l.Download).ToList();
}

internal sealed record ForgeLibraryFile(string Name, NdDowItem Download, string[] Hashes, string? EmbeddedEntry, bool Generated = false);
