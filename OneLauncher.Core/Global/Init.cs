using Microsoft.Extensions.DependencyInjection;
using OneLauncher.Core.Downloader;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.Account.Microsoft;
using OneLauncher.Core.Net.ConnectToolPower;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneLauncher.Core.Global;
public static class Init
{
    public const string ApplicationVersoin = "1.0.0.0";
    public const string ApplicationID = "53740b20-7f24-46a3-82cc-ea0376b9f5b5";
    public const string PackageName = "com.onelauncher.lnetface";
    public const string ProjectWebsite = "https://github.com/zhweaa/OneLauncher";
    public static Task<IServiceCollection> InitTask;
    //public static IServiceCollection Service { get; private set; }
    public static string BasePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneLauncher");
    public static string GameRootPath
#if DEBUG
    { get; set; }
#else
    { get; private set; }
#endif
    public static string InstalledPath { get; private set; }
    public static SystemType SystemType { get; private set; }
    public static List<VersionBasicInfo> MojangVersionList = null;
    public static List<IDisposable> OnApplicationClosingReleaseSourcesList = new();
    // 必要的时候还是得耦合一下的，啥都得传递那他妈的写起来太难受了
    public static DBManager ConfigManger => ConfigManager;
    public static GameDataManager GameDataManger => GameDataManager;
    internal static DBManager ConfigManager { get; private set; }
    internal static AccountManager AccountManager { get; private set; }
    internal static GameDataManager GameDataManager { get; private set; }
    internal static MsalAuthenticator MsalAuthenticator { get; private set; }
    internal static JavaManager JavaManager { get; private set; }
    public static Download Download { get; private set; }
    public static async Task<IServiceCollection> Initialize(bool isCommandMode = false)
    {
        try
        {
            Directory.CreateDirectory(BasePath);

            var services = new ServiceCollection();
            
            // 先把最重要的基本配置信息初始化了，然后初始化别的
            var configManger = new DBManager(Path.Combine(BasePath,"config.json"));
            await configManger.InitializeAsync();
            services.AddSingleton<DBManager>(configManger);
            ConfigManager = configManger;

            InstalledPath = configManger.Data.OlanSettings.InstallPath ?? Path.Combine(BasePath,"installed");
            GameRootPath = InstalledPath == null ? Path.Combine(BasePath, "installed", "minecraft") : Path.Combine(InstalledPath, "minecraft");
            Directory.CreateDirectory(InstalledPath);
            // 初始化系统信息
            SystemType = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? SystemType.windows :
                         RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? SystemType.linux :
                         RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? SystemType.osx : SystemType.linux;

            var accountManger = new AccountManager(Path.Combine(BasePath,"playerdata","account.json"));
            await accountManger.InitializeAsync();
            services.AddSingleton<AccountManager>(accountManger);
            AccountManager = accountManger; 

            var gameDataManger = new GameDataManager(Path.Combine(GameRootPath, "instance", "instance.json"));
            await gameDataManger.InitializeAsync();
            services.AddSingleton<GameDataManager>(gameDataManger);
            GameDataManager = gameDataManger;

            var downloadTool = new Download();
            services.AddSingleton<Download>(downloadTool);
            OnApplicationClosingReleaseSourcesList.Add(downloadTool);
            Download = downloadTool;

            var msrl = await MsalAuthenticator.CreateAsync(ApplicationID);
            services.AddSingleton<MsalAuthenticator>(msrl);
            OnApplicationClosingReleaseSourcesList.Add(msrl);
            MsalAuthenticator = msrl;

            var javaManager = new JavaManager();
            services.AddSingleton<JavaManager>(javaManager);
            JavaManager = javaManager;

            /*
             调试代码
             */
            //ConfigManager.Data.FavoriteServers.Add("zw",new ServerConfig("mc.zhweaa.dpdns.org","25568", "084c3333"));

            return services; 
        }
        #region
        catch (ArgumentException ex)
        {
            throw new OlanException("参数错误", $"参数未被正常传递：{ex}", OlanExceptionAction.FatalError);
        }
        catch (PathTooLongException ex)
        {
            throw new OlanException("路径过长", $"路径过长：{ex}", OlanExceptionAction.FatalError);
        }
        catch (NotSupportedException ex)
        {
            throw new OlanException("不支持的操作", $"当前操作不被支持：{ex}", OlanExceptionAction.FatalError);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new OlanException("权限不足", $"当前用户没有足够的权限：{ex}", OlanExceptionAction.FatalError);
        }
        catch (FileNotFoundException ex)
        {
            throw new OlanException("文件未找到", $"所需文件不存在：{ex}", OlanExceptionAction.FatalError);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new OlanException("目录未找到", $"所需目录不存在：{ex}", OlanExceptionAction.FatalError);
        }
        catch (InvalidOperationException ex)
        {
            throw new OlanException("操作无效", $"当前操作无效：{ex}", OlanExceptionAction.FatalError);
        }
        #endregion
    }
}
