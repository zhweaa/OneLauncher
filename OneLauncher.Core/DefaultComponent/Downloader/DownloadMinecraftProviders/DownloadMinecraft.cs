using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders.DownloadSources;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft;
using OneLauncher.Core.DefaultComponent.Mod.ModLoader.fabric;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;
using OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;


namespace OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
internal partial class DownloadMinecraft
{
    private IDownloadSourceUrlProvider[] _urlProviders;
    internal DownloadInfo info;

    public readonly CancellationToken cancelToken;
    public readonly IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)>? progress;
    public int alls = 0;
    public int dones = 0;
    
    public DownloadMinecraft(
        DownloadInfo info,
        
        IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress,
        CancellationToken? cancelToken = null
        )
    {
        this.info = info;
        this.progress = progress;
        this.cancelToken = cancelToken ?? CancellationToken.None;

        _urlProviders = info.DownloadStrategy switch
        { 
            // 必须保证一个是官方源
            DownloadSourceStrategy.OfficialOnly => 
            [
                new MojangSourceDownloadUrlGetter()
            ],
            DownloadSourceStrategy.RaceWithBmcl => 
            [
                new MojangSourceDownloadUrlGetter(),
                new BmlcSourceDownloadUrlGetter(info.ID)
            ],
            DownloadSourceStrategy.RaceWithOlan => 
            [
                new MojangSourceDownloadUrlGetter(),
                new OlanSourceDownloadUrlGetter()
            ],
            _ => throw new OlanException("内部错误", "未知的下载策略")
        };
    }

    public async Task MinecraftBasic()
    {
        // 1. 启动后台任务：如果需要，则开始下载Java
        Task javaInstallTask = info.AndJava ? JavaInstallTasker() : Task.CompletedTask;

        // 2. 生成下载计划
        progress?.Report((DownProgress.Meta, 0, 0, "正在生成下载计划..."));
        var plan = await CreateDownloadPlan();

        // 3. 初始化进度报告
        alls = plan.AllFilesGoVerify.Count;
        progress?.Report((DownProgress.Meta, alls, dones, "下载计划生成完毕，开始下载..."));

        if (cancelToken.IsCancellationRequested)
            return;

        // 4. 执行核心下载任务

        await DownloadClientTasker(plan.ClientMainFile);
        if (cancelToken.IsCancellationRequested)
            return;
        // 模组加载器的下载和安装是后台任务
        Task modInstallTasker = 
            plan.ModProviders != null && (info.UserInfo.Select(x => x.ModLoader == ModEnum.none ? false : true).All(x => x == true))
            ? UnityModsInstallTasker(plan.ModLoaderFiles, plan.ModProviders,javaInstallTask)
            : Task.CompletedTask;
        
        await DownloadLibrariesSupportTasker(plan.LibraryFiles);
        if (cancelToken.IsCancellationRequested)
            return;
        await DownloadAssetsSupportTasker(plan.AssetFiles);
        if (cancelToken.IsCancellationRequested)
            return;


        if (plan.LoggingFile.HasValue)
            await LogginInstallTasker(plan.LoggingFile.Value);

        if (cancelToken.IsCancellationRequested)
            return;

        // 5. 等待后台任务完成
        await modInstallTasker;
        await javaInstallTask;

        if (cancelToken.IsCancellationRequested)
            return;

        // 6. (可选)校验所有文件
        if (info.IsSha1)
        {
            progress?.Report((DownProgress.Verify, alls, alls, "校验中...")); 
            await info.DownloadTool.CheckAllSha1(plan.AllFilesGoVerify, info.MaxSha1Threads, cancelToken);
        }

        // 公共安装服务保存版本索引后再报告最终完成。
    }
}
