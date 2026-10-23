using OneLauncher.Core.DefaultComponent.Account.Microsoft;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using OneLauncher.Core.Services;

namespace OneLauncher.Core.Global;
public class UnitedCore
{
    internal static UnitedCore Core { get; private set; } = null!;
    public required TaskManager TaskManager { get; init; }
    public required VersionInstallerService VersionInstallerService { get; init; }
    public required VersionListManager VersionListManager { get; init; }
    public required ResourceInstallerService ResourceInstallerService { get; init; }
    public required GameLaunchService GameLaunchService { get; init; }
    public required List<VersionBasicInfo> MojangVersionList { get; set; }
    public required AccountManager AccountManager { get; init; }
    public required GameDataManager GameDataManager { get; init; }
    public required MsalAuthenticator MsalAuthenticator { get; init; }
    public required JavaManager JavaManager { get; init; }
    public static async Task<UnitedCore> Initialize()
    {
        var versionListManager = new VersionListManager(Init.GameRootPath);
        await versionListManager.InitializeAsync();

        var taskManager = new TaskManager();
        var accountManger = new AccountManager(Path.Combine(Init.BasePath, "playerdata", "account.json"));
        await accountManger.InitializeAsync();

        var gameDataManger = new GameDataManager(Path.Combine(Init.GameRootPath, "instance", "instance.json"));
        await gameDataManger.InitializeAsync();

        var msrl = await MsalAuthenticator.CreateAsync(Init.ApplicationID);
        Init.OnApplicationClosingReleaseSourcesList.Add(msrl);

        var javaManager = new JavaManager();
        var core = new UnitedCore()
        {
            TaskManager = taskManager,
            VersionListManager = versionListManager,
            VersionInstallerService = new VersionInstallerService(versionListManager),
            ResourceInstallerService = new ResourceInstallerService(),
            GameLaunchService = new GameLaunchService(taskManager),
            MojangVersionList = new (await VersionsList.GetOrRefreshVersionListAsync()),
            AccountManager = accountManger,
            GameDataManager = gameDataManger,
            MsalAuthenticator = msrl,
            JavaManager = javaManager
        };
        Core = core;
        return core;
    }
}
