using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Services;

// 使用隔离目录和 HTTP 替身，不初始化真实账户、不连接外网。
string output = Path.GetFullPath(args.FirstOrDefault() ?? "build/resource-tests");
string root = Path.Combine(output, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Init.GameRootPath = Path.Combine(root, "minecraft");
var config = new DBManager(Path.Combine(root, "config.json"));
await config.InitializeAsync();
typeof(Init).GetProperty("ConfigManager", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, config);

using var handler = new FixtureHandler();
using var client = new HttpClient(handler);
var downloadType = typeof(Init).Assembly.GetType("OneLauncher.Core.DefaultComponent.Downloader.Download", true)!;
using var download = (IDisposable)Activator.CreateInstance(downloadType, client)!;
typeof(Init).GetProperty("Download", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, download);
var service = (ResourceInstallerService)Activator.CreateInstance(typeof(ResourceInstallerService), nonPublic: true)!;
var instance = new GameData("Shader test", "1.21.1", ModEnum.none, Guid.Empty);
var progress = new RecordedProgress();
string target = Path.Combine(instance.InstancePath, "shaderpacks", "shader.zip");

await service.InstallShaderPack(progress, "shader-project", instance);
Assert(File.ReadAllBytes(target).SequenceEqual(FixtureHandler.Content), "光影文件保存到 shaderpacks");
Assert(!Directory.Exists(Path.Combine(instance.InstancePath, "mods")), "无加载器实例也可安装，未安装依赖");
Assert(handler.Requests.Count == 2, "只请求主项目版本和主文件，不请求 Iris 依赖或附加文件");
Assert(handler.Requests[0].Query.Contains("game_versions="), "版本请求包含 Minecraft 版本筛选");
Assert(Uri.UnescapeDataString(handler.Requests[0].Query).Contains("[\"1.21.1\"]"), "筛选的版本来自实例");
Assert(!handler.Requests[0].Query.Contains("loaders"), "没有实例加载器筛选");
Assert(progress.IsComplete, "成功安装最后报告 100%");

int downloads = handler.Downloads;
progress.Clear();
await service.InstallShaderPack(progress, "shader-project", instance);
Assert(handler.Downloads == downloads && progress.IsComplete, "重复安装跳过现有文件并校验、报告完成");

await File.WriteAllBytesAsync(target, new byte[FixtureHandler.Content.Length]);
progress.Clear();
await ExpectFailure<OlanException>(() => service.InstallShaderPack(progress, "shader-project", instance));
Assert(!progress.IsComplete, "已存在但 SHA1 错误的文件不会报告完成");
await service.InstallShaderPack(null, "shader-project", instance, verifySha1: false);
Assert(handler.Downloads == downloads, "关闭 SHA1 时跳过现有同长度文件，支持空进度对象");

// 明显残缺的已有文件应由下载器重新下载。
await File.WriteAllBytesAsync(target, [0]);
await service.InstallShaderPack(null, "shader-project", instance);
Assert(handler.Downloads == downloads + 1 && File.ReadAllBytes(target).SequenceEqual(FixtureHandler.Content), "残缺文件重新下载");

foreach (var loader in new[] { ModEnum.fabric, ModEnum.forge, ModEnum.neoforge, ModEnum.quilt })
{
    instance.ModLoader = loader;
    await service.InstallShaderPack(null, "shader-project", instance);
}
Assert(handler.Requests.All(uri => !uri.Query.Contains("loaders")), "各类实例加载器都不影响光影请求");

handler.Metadata = "[]";
progress.Clear();
await ExpectFailure<OlanException>(() => service.InstallShaderPack(progress, "shader-project", instance));
Assert(!progress.IsComplete, "没有兼容版本明确失败");
handler.Metadata = "[{\"files\":[]}]";
await ExpectFailure<OlanException>(() => service.InstallShaderPack(null, "shader-project", instance));
handler.Metadata = "not json";
await ExpectFailure<JsonException>(() => service.InstallShaderPack(null, "shader-project", instance));
handler.StatusCode = HttpStatusCode.NotFound;
await ExpectFailure<HttpRequestException>(() => service.InstallShaderPack(null, "shader-project", instance));
handler.StatusCode = HttpStatusCode.OK;

foreach (string filename in new[] { "../outside.zip", "..\\outside.zip", "C:\\outside.zip" })
{
    handler.Metadata = FixtureHandler.Version(filename);
    await ExpectFailure<OlanException>(() => service.InstallShaderPack(null, "shader-project", instance));
}
Assert(!File.Exists(Path.Combine(instance.InstancePath, "outside.zip")), "拒绝越界文件名");

// API 不标记 primary 时回退到第一个文件。
handler.Metadata = FixtureHandler.Version("fallback.zip", primary: false, includeExtra: false);
await service.InstallShaderPack(null, "shader-project", instance);
Assert(File.Exists(Path.Combine(instance.InstancePath, "shaderpacks", "fallback.zip")), "没有 primary 时仍可安装");

handler.Metadata = FixtureHandler.Version("metadata-cancel.zip");
using (var cancellation = new CancellationTokenSource())
{
    handler.OnRequest = uri => { if (uri.Host == "api.modrinth.com") cancellation.Cancel(); };
    progress.Clear();
    await ExpectFailure<OperationCanceledException>(() => service.InstallShaderPack(progress, "shader-project", instance, cancellationToken: cancellation.Token));
    Assert(!progress.IsComplete, "获取元数据时取消保持取消状态");
}

handler.Metadata = FixtureHandler.Version("download-cancel.zip");
using (var cancellation = new CancellationTokenSource())
{
    handler.OnRequest = uri => { if (uri.Host == "files.invalid") cancellation.Cancel(); };
    progress.Clear();
    await ExpectFailure<OperationCanceledException>(() => service.InstallShaderPack(progress, "shader-project", instance, verifySha1: false, cancellationToken: cancellation.Token));
    Assert(!progress.IsComplete, "关闭校验时下载取消也不会报告完成");
    Assert(!File.Exists(Path.Combine(instance.InstancePath, "shaderpacks", "download-cancel.zip")), "取消未发布目标文件");
}
handler.OnRequest = null;

using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    int requests = handler.Requests.Count;
    await ExpectFailure<OperationCanceledException>(() => service.InstallShaderPack(null, "shader-project", instance, cancellationToken: cancellation.Token));
    Assert(handler.Requests.Count == requests, "提前取消不发起请求");
}

// 原 Mod 入口继续复用相同下载与校验流程。
handler.Metadata = FixtureHandler.Version("mod.jar", includeExtra: false, includeDependencies: false);
await service.InstallMod(null!, "shader-project", instance, ModEnum.fabric, includeDependencies: false);
Assert(File.Exists(Path.Combine(instance.InstancePath, "mods", "mod.jar")), "Mod 安装入口仍写入 mods");
Assert(handler.Requests[^2].Query.Contains("loaders="), "Mod 版本查询仍按加载器筛选");
Console.WriteLine("PASS shader installation, primary/fallback file, dependency skipping, loader independence, existing files, SHA1, invalid metadata, path validation, cancellation and Mod regression");
Console.WriteLine("Fixtures: " + root);

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static async Task ExpectFailure<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}

sealed class RecordedProgress : IProgress<(long AllFiles, long DownedFiles, double Percentage, string DowingFileName)>
{
    private readonly List<(long AllFiles, long DownedFiles, double Percentage, string DowingFileName)> _values = [];
    public bool IsComplete => _values.Count > 0 && _values[^1].Percentage == 100 && _values[^1].DowingFileName == "处理完成！";
    public void Clear() => _values.Clear();
    public void Report((long AllFiles, long DownedFiles, double Percentage, string DowingFileName) value) => _values.Add(value);
}

sealed class FixtureHandler : HttpMessageHandler
{
    public static readonly byte[] Content = Encoding.UTF8.GetBytes("shader fixture");
    public string Metadata { get; set; } = Version("shader.zip");
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public List<Uri> Requests { get; } = [];
    public int Downloads { get; private set; }
    public Action<Uri>? OnRequest { get; set; }

    public static string Version(string filename, bool primary = true, bool includeExtra = true, bool includeDependencies = true)
    {
        string name = JsonEncodedText.Encode(filename).ToString();
        string hash = Convert.ToHexStringLower(SHA1.HashData(Content));
        string extra = includeExtra ? "{\"url\":\"https://extra.invalid/unwanted\",\"filename\":\"extra.zip\",\"size\":1}," : "";
        string dependencies = includeDependencies ? "{\"project_id\":\"iris-project\"}" : "";
        return $$$"""
        [{"name":"Latest","files":[{{{extra}}}{"url":"https://files.invalid/main","filename":"{{{name}}}","size":{{{Content.Length}}},"primary":{{{(primary ? "true" : "false")}}},"hashes":{"sha1":"{{{hash}}}"}}],"dependencies":[{{{dependencies}}}]}]
        """;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add(uri);
        OnRequest?.Invoke(uri);
        cancellationToken.ThrowIfCancellationRequested();
        HttpContent content;
        if (uri.Host == "api.modrinth.com" && uri.AbsolutePath == "/v2/project/shader-project/version")
            content = new StringContent(Metadata, Encoding.UTF8, "application/json");
        else if (uri.Host == "files.invalid")
        {
            Downloads++;
            content = new ByteArrayContent(Content);
        }
        else
            throw new Exception("Unexpected dependency or additional file request: " + uri);
        return Task.FromResult(new HttpResponseMessage(StatusCode) { Content = content });
    }
}
