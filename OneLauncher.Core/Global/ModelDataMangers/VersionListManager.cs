using OneLauncher.Core.Helper.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OneLauncher.Core.Global.ModelDataMangers;

public sealed class VersionListData
{
    [JsonRequired]
    public List<UserVersion> VersionList { get; set; } = new();
}

[JsonSerializable(typeof(VersionListData))]
[JsonSerializable(typeof(UserVersion))]
[JsonSerializable(typeof(ModType))]
public partial class VersionListJsonContext : JsonSerializerContext { }

/// <summary>当前 Minecraft 根目录的已安装版本索引，由 UnitedCore 创建并持有。</summary>
public sealed class VersionListManager : BasicDataManager<VersionListData>
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _stateLock = new();
    public string GameRootPath { get; }
    public string IndexPath => ConfigPath;
    protected override bool RequireValidDocument => true;

    public VersionListManager(string gameRootPath)
        : base(Path.Combine(gameRootPath, "versions", "version_list.json"))
    {
        GameRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRootPath));
    }

    public List<UserVersion> GetVersions()
    {
        lock (_stateLock)
            return Data.VersionList.Select(Clone).ToList();
    }

    public UserVersion? FindVersion(string versionId)
    {
        lock (_stateLock)
        {
            var version = Data.VersionList.FirstOrDefault(x => x.VersionID == versionId);
            return version == null ? null : Clone(version);
        }
    }

    public async Task RegisterInstalledVersionAsync(UserVersion installedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        if (!IsValidVersionId(installedVersion.VersionID))
            throw new OlanException("版本登记失败", "版本 ID 必须是有效的单级目录名。");
        var incoming = Clone(installedVersion);
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var versions = GetVersions();
            MergeVersion(versions, incoming);
            cancellationToken.ThrowIfCancellationRequested();
            await CommitAsync(versions);
        }
        finally { _operationLock.Release(); }
        NotifyDataChanged();
    }

    public async Task ReplaceOrderAsync(IReadOnlyList<UserVersion> orderedVersions)
    {
        var requestedIds = orderedVersions.Select(x => x.VersionID).ToArray();
        await _operationLock.WaitAsync();
        try
        {
            var current = GetVersions();
            var byId = current.ToDictionary(x => x.VersionID, StringComparer.Ordinal);
            var reordered = new List<UserVersion>();
            foreach (var id in requestedIds)
                if (byId.Remove(id, out var version)) reordered.Add(version);
            // Preserve versions registered since the page took its snapshot.
            reordered.AddRange(current.Where(x => byId.ContainsKey(x.VersionID)));
            await CommitAsync(reordered);
        }
        finally { _operationLock.Release(); }
        NotifyDataChanged();
    }

    public void EnsureGameRoot(string gameRootPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRootPath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(root, GameRootPath, comparison))
            throw new OlanException("安装目录不匹配",
                $"版本索引属于 {GameRootPath}，不能用于 {root}。请重新初始化对应安装目录。");
    }

    private async Task CommitAsync(List<UserVersion> versions)
    {
        var candidate = new VersionListData { VersionList = versions };
        await SaveDataAsync(candidate);
        lock (_stateLock) Data = candidate;
    }

    internal static bool IsValidVersionId(string? versionId) =>
        !string.IsNullOrWhiteSpace(versionId) && versionId is not "." and not ".." &&
        versionId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        versionId.IndexOfAny(['/', '\\', ':']) < 0 &&
        !versionId.EndsWith('.') && !versionId.EndsWith(' ');

    internal static UserVersion Clone(UserVersion version) => new()
    {
        VersionID = version.VersionID,
        modType = version.modType,
        AddTime = version.AddTime
    };

    internal static void MergeVersion(List<UserVersion> versions, UserVersion incoming)
    {
        var existing = versions.FirstOrDefault(x => x.VersionID == incoming.VersionID);
        if (existing == null)
        {
            versions.Add(Clone(incoming));
            return;
        }
        var flags = existing.modType;
        flags.IsFabric |= incoming.modType.IsFabric;
        flags.IsForge |= incoming.modType.IsForge;
        flags.IsNeoForge |= incoming.modType.IsNeoForge;
        flags.IsQuilt |= incoming.modType.IsQuilt;
        existing.modType = flags;
    }

    protected override void ValidateData(VersionListData data)
    {
        if (data.VersionList == null)
            throw new JsonException("VersionList 不能为 null。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var version in data.VersionList)
        {
            if (version == null || !IsValidVersionId(version.VersionID))
                throw new JsonException("VersionList 包含无效的版本 ID。");
            if (!ids.Add(version.VersionID))
                throw new JsonException($"VersionList 包含重复版本：{version.VersionID}。");
        }
    }

    protected override JsonTypeInfo<VersionListData> CreateJsonTypeInfo(JsonSerializerOptions options)
        => new VersionListJsonContext(options).VersionListData;
}
