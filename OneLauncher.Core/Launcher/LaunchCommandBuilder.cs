using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Launcher.Strategys;
using OneLauncher.Core.Minecraft;
using System.Runtime.CompilerServices;
namespace OneLauncher.Core.Launcher;
public partial class LaunchCommandBuilder
{
    // 依赖
    private readonly JavaManager _javaManager = Init.JavaManager;
    // 构造依赖
    public VersionInfomations versionInfo;
    private readonly char separator;
    private readonly string versionId;
    private readonly string basePath;
    private readonly string versionPath;
    // 下面是外部注入的
    private string gamePath;
    private IEnumerable<string>? extraJvmArgs = null;
    private IEnumerable<string>? extraGameArgs = null;
    private List<string> commandArgs = new();
    private ModEnum modType = ModEnum.none;
    private ServerInfo? serverInfo = null;
    private UserModel loginUser;
    private bool usesImportedManifest;

    // 构造函数保持不变
    private LaunchCommandBuilder(
        string basePath,
        string versionId)
    {
        this.basePath = basePath;
        this.versionPath = Path.Combine(this.basePath, "versions", versionId);
        this.versionId = versionId;
#if ONELAUNCHER_TARGET_WINDOWS
        separator = ';';
#else
    separator = ':';
#endif
        #region 对于未Set的默认值
        this.gamePath = this.basePath; // 默认游戏路径为根目录
        #endregion
    }
    #region 链式调用
    /// <param name="basePath">游戏基本路径（minecraft）</param>
    /// <param name="versionId">启动游戏的基本版本ID（如1.21.5）</param>
    public static async Task<LaunchCommandBuilder> CreateAsync(string basePath, string versionId, string? importedManifestPath = null)
    {
        return new LaunchCommandBuilder(basePath, versionId)
        {
            versionInfo = new VersionInfomations(
            await File.ReadAllTextAsync(importedManifestPath ?? Path.Combine(basePath,"versions",versionId, "version.json")),
            basePath),
            usesImportedManifest = importedManifestPath != null
        };
    }
    /// <summary>设置传递给游戏的游戏目录</summary>
    public LaunchCommandBuilder SetGamePath(string gamePath)
    {
        this.gamePath = gamePath;
        return this;
    }
    /// <summary>
    /// 设置以哪个模组加载器启动游戏
    /// （！）必须设置已经安装的模组加载器
    /// （？）如果不设置，则默认为原版Minecraft
    /// </summary>
    public LaunchCommandBuilder SetModType(ModEnum modType)
    {
        this.modType = modType;
        return this;
    }
    /// <summary>设置登入用户，如果不设置则为全局默认值</summary>
    public LaunchCommandBuilder SetLoginUser(UserModel user)
    {
        this.loginUser = user;
        return this;
    }
    /// <summary>设置必须在命令行传递的JVM参数</summary>
    public LaunchCommandBuilder SetCommandArgs(IEnumerable<string> args)
    {
        commandArgs.AddRange(args);
        return this;
    }
    /// <summary>设置额外的JVM参数</summary>
    public LaunchCommandBuilder WithExtraJvmArgs(IEnumerable<string> args)
    {
        extraJvmArgs = args;
        return this;
    }
    /// <summary>设置额外的游戏参数</summary>
    public LaunchCommandBuilder WithExtraGameArgs(IEnumerable<string> args)
    {
        extraGameArgs = args;
        return this;
    }
    /// <summary>设置服务器信息，如果不设置则不连接服务器</summary>
    public LaunchCommandBuilder WithServerInfo(ServerInfo? serverInfo)
    {
        this.serverInfo = serverInfo;
        return this;
    }
    #endregion
    public string GetJavaPath() =>
        _javaManager.GetJavaExecutablePath(versionInfo.GetJavaVersion());
    public async Task<LaunchCommand> BuildCommand()
    {
        if (usesImportedManifest)
        {
            loginUser ??= Init.AccountManager.GetDefaultUser();
            ConfigureAuthentication();
            var importedArgs = new List<string>();
            if (extraJvmArgs != null) importedArgs.AddRange(extraJvmArgs);
            importedArgs.AddRange(ImportedLaunchArguments.Build(versionInfo, gamePath, loginUser, Init.ApplicationVersoin, serverInfo)
                .Select(ImportedLaunchArguments.Quote));
            if (extraGameArgs != null) importedArgs.AddRange(extraGameArgs);
            return new LaunchCommand(importedArgs, commandArgs);
        }
        IModArgStrategy? strategy = null; // 策略可以是null，代表原版
        if (modType != ModEnum.none)
            strategy = await CreateAndInitStrategy();

        // 确定主类：如果策略提供了覆盖，则使用策略的；否则用原版的
        string mainClass = strategy?.GetMainClassOverride() ?? versionInfo.GetMainClass();

        // 将策略传递给各个构建器
        List<string> rargs = new List<string>();
        if (extraJvmArgs != null)
            rargs.AddRange(extraJvmArgs);
        rargs.AddRange(BuildJvmArgs(strategy));
        rargs.Add(strategy?.GetMainClassOverride() ?? versionInfo.GetMainClass()); // 别把主类忘了
        rargs.AddRange(BuildGameArgs(gamePath ,strategy));
        return new LaunchCommand(rargs, commandArgs);
    }
    // 策略方法
    private async Task<IModArgStrategy?> CreateAndInitStrategy()
    {
        IModArgStrategy strategy = modType switch
        {
            ModEnum.fabric => await FabricStrategy.CreateAsync(versionPath, basePath),
            ModEnum.quilt => await QuiltStrategy.CreateAsync(versionPath, basePath),
            ModEnum.neoforge => await NeoForgeStrategy.CreateAsync(versionPath, basePath, versionId),
            ModEnum.forge => await ForgeStrategy.CreateAsync(versionPath, basePath, versionId),
            _ => throw new OlanException("内部异常",$"不支持的模组加载器 {modType}")
        };
        return strategy;
    }
}
