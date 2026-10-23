using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Server;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace OneLauncher.Core.Global.ModelDataMangers;
[JsonSerializable(typeof(JvmArguments))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(DefaultConfig))]
[JsonSerializable(typeof(UserModel))]
[JsonSerializable(typeof(UserVersion))]
[JsonSerializable(typeof(ServerEntry))]
[JsonSerializable(typeof(ModType))]
[JsonSerializable(typeof(ModEnum))]
[JsonSerializable(typeof(HomePageStyle))]
public partial class OneLauncherAppConfigsJsonContext : JsonSerializerContext { }
public enum HomePageStyle
{
    Classic = 0,
    MagicButton = 1
}
public class DefaultConfig
{
    public JvmArguments MinecraftJvmArguments { get; set; } = JvmArguments.CreateFromMode();
    public string CrafatarUrl { get; set; } = "http://106.55.4.15:18792/renders/body/";
    public string? ApiKey { get; set; } = null;
    public List<string> RecommandedModsForServer { get; set; } = new List<string>();
    public List<string> RecommandedModsForSurvival { get; set; } = new List<string>();
}
public class AppSettings
{
    // 下载
    public int MaximumDownloadThreads { get; set; } = 24;
    public int MaximumSha1Threads { get; set; } = 24;
    public bool IsSha1Enabled { get; set; } = true;
    public DownloadSourceStrategy DownloadMinecraftSourceStrategy { get; set; } = DownloadSourceStrategy.OfficialOnly;
    public string? InstallPath { get; set; }
    public string? DefaultInstanceID { get; set; }
    public Guid? DefaultServerID { get; set; }
    // 主页默认启动项为服务器收藏时记录其 Id；为 null 时主页使用 DefaultInstanceID 对应的游戏数据。
    public Guid? HomeDefaultServerID { get; set; }
    // 每天一更新
    public DateTimeOffset LastVersionManifestRefreshTime { get; set; } = DateTimeOffset.UtcNow;
    public int LeftButtonListSpacing { get; set; } = 8;
    public bool ShowIdentifiers { get; set; } = true;
    // 主页和设置始终显示，不提供可关闭的配置。
    public bool IsVersionPageVisible { get; set; } = false;
    public bool IsGameDataPageVisible { get; set; } = true;
    public bool IsServerPageVisible { get; set; } = true;
    public bool IsDownloadPageVisible { get; set; } = true;
    public bool IsModsBrowserPageVisible { get; set; } = true;
    public bool IsAccountPageVisible { get; set; } = true;
    // 主页样式；板块可见性只在魔法按钮样式下生效，默认样式始终显示全部板块。
    public HomePageStyle HomePageStyle { get; set; } = HomePageStyle.Classic;
    public bool IsHomeNewsVisible { get; set; } = true;
    public bool IsHomeTaskCenterVisible { get; set; } = true;
    public bool IsHomePowerPlayVisible { get; set; } = true;
    public bool IsHomeImportVisible { get; set; } = true;
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public class AppConfig
{
    // 除了系统自带的Java以外启动器安装的所有Java版本列表
    public Dictionary<int,string?> AvailableJavas { get; set; } = new();
    // 玩家收藏的所有服务器列表，内部对应GameData
    public List<ServerEntry> ServerList { get; set; } = new();
    public AppSettings OlanSettings { get; set; } = new AppSettings();
    public DefaultConfig DefaultSettings { get; set; } = new DefaultConfig();
}
public class DBManager : BasicDataManager<AppConfig>
{
    public DBManager(string configPath)
        :base(configPath)
    {
    }
    protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<AppConfig> CreateJsonTypeInfo(JsonSerializerOptions options)
        => new OneLauncherAppConfigsJsonContext(options).AppConfig;
}
