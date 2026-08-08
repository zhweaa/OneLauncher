using OneLauncher.Core.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.Server;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace OneLauncher.Core.Global.ModelDataMangers;
[JsonSerializable(typeof(JvmArguments))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(UserModel))]
[JsonSerializable(typeof(UserVersion))]
[JsonSerializable(typeof(ServerEntry))]
[JsonSerializable(typeof(ModType))]
[JsonSerializable(typeof(ModEnum))]
public partial class OneLauncherAppConfigsJsonContext : JsonSerializerContext { }
public class AppSettings
{
    public JvmArguments MinecraftJvmArguments { get; set; } = JvmArguments.CreateFromMode();
    // 下载
    public int MaximumDownloadThreads { get; set; } = 24;
    public int MaximumSha1Threads { get; set; } = 24;
    public bool IsSha1Enabled { get; set; } = true;
    public DownloadSourceStrategy DownloadMinecraftSourceStrategy { get; set; } = DownloadSourceStrategy.OfficialOnly;
    public string? InstallPath { get; set; }
    public string? ApiKey { get; set; } = null;
    public string CrafatarUrl { get; set; } = "127.0.0.1:3000";
    public string? DefaultInstanceID { get; set; }
    public Guid? DefaultServerID { get; set; }
    // 每天一更新
    public DateTimeOffset LastVersionManifestRefreshTime { get; set; } = DateTimeOffset.UtcNow;
    public int LeftButtonListSpacing { get; set; } = 8;
    public bool IsServerPageVisible { get; set; } = false;
    public bool IsVersionPageVisible { get; set; } = true;
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public class AppConfig
{
    // 除了系统自带的Java以外启动器安装的所有Java版本列表
    public Dictionary<int,string?> AvailableJavas { get; set; } = new();
    // 当前启动器已安装的所有版本列表，默认初始化为空列表
    public List<UserVersion> VersionList { get; set; } = new ();
    // 玩家收藏的所有服务器列表，内部对应GameData
    public List<ServerEntry> ServerList { get; set; } = new();
    public AppSettings OlanSettings { get; set; } = new AppSettings();
}
public class DBManager : BasicDataManager<AppConfig>
{
    public DBManager(string configPath)
        :base(configPath)
    {
    }
    protected override JsonSerializerContext GetJsonContext()
        => OneLauncherAppConfigsJsonContext.Default;
}
