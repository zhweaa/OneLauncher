using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Minecraft.JsonModels;
using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace OneLauncher.Core.Minecraft;

/// <summary>
/// 表示Minecraft版本信息的解析器，用于解析version.json文件并提取关键信息。
/// 支持动态解析依赖库、资源索引等。
/// </summary>
public class VersionInfomations
{
    public readonly MinecraftVersionInfo info;
    public readonly string basePath;
    public List<string> NativesLibs = new List<string>();

    /// <summary>
    /// version.json 文件解析器构造函数。
    /// </summary>
    /// <param Name="json">json文件内容</param>
    /// <param Name="basePath">游戏资本路径（含.minecraft）</param>
    /// <param Name="OsType">系统类型</param>
    /// <param Name="IsVersionInsulation">是否启用了版本隔离</param>
    /// <exception cref="InvalidOperationException">当json解析出错时</exception>
    public VersionInfomations(string json, string basePath)
    {
        this.basePath = basePath;

        info = JsonSerializer.Deserialize<MinecraftVersionInfo>(json, MinecraftJsonContext.Default.MinecraftVersionInfo);
    }

    /// <summary>
    /// 获取所有需要下载的库文件，并自动识别和填充需要解压的原生库列表。
    /// 此方法使用编译时预处理指令为特定平台进行优化。
    /// </summary>
    /// <returns>一个包含所有需要下载的库文件的列表。</returns>
    public List<NdDowItem> GetLibraries()
    {
        var libraries = new List<NdDowItem>();
        NativesLibs.Clear();

        // 编译时确定的常量
#if WINDOWS
        const string osName = "windows";
        const string archName = "x64";
#elif MACOS
    const string osName = "osx";
    const string archName = "arm64";
#else // Linux
    const string osName = "linux";
    const string archName = "x64";
#endif

        foreach (var lib in info.Libraries)
        {
            // 1. 内联的规则检查 (现在可以正确访问 rule.Os.Arch)
            bool isAllowed = true;
            if (lib.Rules != null && lib.Rules.Count > 0)
            {
                var lastAction = "disallow";
                foreach (var rule in lib.Rules)
                {
                    bool conditionMet = false;
                    if (rule.Os != null)
                    {
                        if ((rule.Os.Name == null || rule.Os.Name == osName) &&
                            (rule.Os.Arch == null || rule.Os.Arch == archName))
                        {
                            conditionMet = true;
                        }
                    }
                    else { conditionMet = true; }

                    if (conditionMet) { lastAction = rule.Action; }
                }
                isAllowed = (lastAction == "allow");
            }
            if (!isAllowed) continue;

            // 2. 识别并准备原生库
            MinecraftLibraryArtifact nativeArtifact = null;
            bool isModernNative = false;

            // **方式A: 优先处理旧版JSON的 "natives" 对象**
            // (现在可以正确访问 lib.Natives)
            if (lib.Natives != null && lib.Downloads?.Classifiers != null)
            {
                if (lib.Natives.TryGetValue(osName, out var classifierKey))
                {
                    // 兼容1.16.5的 "${arch}" 占位符
                    classifierKey = classifierKey.Replace("${arch}", "64");
                    lib.Downloads.Classifiers.TryGetValue(classifierKey, out nativeArtifact);
                }
            }

            // **方式B: 处理新版JSON的 "name" 后缀**
            if (lib.Name.Contains($":natives-{osName}"))
            {
                isModernNative = true;
            }

            // 3. 添加主库文件
            if (lib.Downloads?.Artifact != null)
            {
                var libPath = Path.Combine(basePath, "libraries", Path.Combine(lib.Downloads.Artifact.Path.Split('/')));
                libraries.Add(new NdDowItem(
                    Url: lib.Downloads.Artifact.Url,
                    Path: libPath,
                    Size: (int)lib.Downloads.Artifact.Size,
                    Sha1: lib.Downloads.Artifact.Sha1
                ));

                // 如果是新式原生库，它的主 artifact 就是原生库本身
                if (isModernNative)
                {
                    NativesLibs.Add(libPath);
                }
            }

            // 4. 添加旧式原生库文件 (如果通过方式A找到)
            if (nativeArtifact != null)
            {
                var nativePath = Path.Combine(basePath, "libraries", Path.Combine(nativeArtifact.Path.Split('/')));
                // 确保不重复添加
                if (!libraries.Any(l => l.path == nativePath))
                {
                    libraries.Add(new NdDowItem(
                        Url: nativeArtifact.Url,
                        Path: nativePath,
                        Size: (int)nativeArtifact.Size,
                        Sha1: nativeArtifact.Sha1
                    ));
                }
                NativesLibs.Add(nativePath);
            }
        }
        return libraries;
    }
    /// <summary>
    /// 获取当前版本在启动时需要加载到类路径的库文件。
    /// 此方法经过优化，直接返回一个字典以提高后续处理效率。
    /// </summary>
    /// <returns>以 Maven 坐标为键、库文件完整路径为值的字典；分类器会作为键的一部分。</returns>
    public Dictionary<string, string> GetLibraryiesForUsing()
    {
        var libraries = new Dictionary<string, string>(info.Libraries.Count);
#if WINDOWS
        const string osName = "windows";
        const string archName = "x64";
#elif MACOS
        const string osName = "osx";
        const string archName = "arm64";
#else // Linux
        const string osName = "linux";
        const string archName = "x64";
#endif
        foreach (var lib in info.Libraries)
        {
            bool isAllowed = true;
            if (lib.Rules != null && lib.Rules.Count > 0)
            {
                var lastAction = "disallow";
                foreach (var rule in lib.Rules)
                {
                    bool conditionMet = false;
                    if (rule.Os != null)
                    {
                        if ((rule.Os.Name == null || rule.Os.Name == osName) &&
                            (rule.Os.Arch == null || rule.Os.Arch == archName))
                        {
                            conditionMet = true;
                        }
                    }
                    else { conditionMet = true; }

                    if (conditionMet) { lastAction = rule.Action; }
                }
                isAllowed = (lastAction == "allow");
            }

            if (!isAllowed)
                continue;

            if (lib?.Downloads?.Artifact != null)
            {
                var parts = lib.Name.Split(':');
                // 分类器是独立的类路径项，不能与同一 artifact 的普通库相互覆盖。
                var libKey = parts.Length >= 4
                    ? $"{parts[0]}:{parts[1]}:{parts[3]}"
                    : $"{parts[0]}:{parts[1]}";

                var libPath = Path.Combine(basePath, "libraries", Path.Combine(lib.Downloads.Artifact.Path.Split('/')));

                // 使用TryAdd，确保只有第一个遇到的同名库被添加，符合原版逻辑
                libraries.TryAdd(libKey, libPath);
            }
        }
        return libraries;
    }
    /// <summary>
    /// 获取版本主文件下载地址。
    /// </summary>
    /// <param ID="version">Minecraft版本号。</param>
    public NdDowItem GetMainFile()
    {
        var t = new NdDowItem(
            Url: info.Downloads.Client.Url,
            Sha1: info.Downloads.Client.Sha1,
            Size: (int)info.Downloads.Client.Size,
            Path: Path.Combine(basePath, "versions", info.ID, $"{info.ID}.jar")
        );
        return t;
    }

    /// <summary>
    /// 获取版本资源索引文件下载地址。
    /// </summary>
    public NdDowItem GetAssets()
    {
        return new NdDowItem(
            Url: info.AssetIndex.Url,
            Path: Path.Combine(basePath, "assets", "indexes", $"{info.AssetIndex.Id}.json"),
            Size: (int)info.AssetIndex.Size,
            Sha1: info.AssetIndex.Sha1
        );
    }

    /// <summary>
    /// 获取资源索引的版本ID。
    /// </summary>
    public string GetAssetIndexVersion()
    {
        return info.AssetIndex.Id;
    }

    /// <summary>
    /// 获取版本的主类名。
    /// </summary>
    /// <returns>主类名（例如"net.minecraft.client.main.Main"）。</returns>
    public string GetMainClass()
    {
        return info.MainClass;
    }

    /// <summary>
    /// 获取日志配置文件信息。
    /// </summary>
    public NdDowItem? GetLoggingConfig()
    {
        if (info.Logging?.Client?.File == null)
            return null;
        return new NdDowItem(
            Url: info.Logging.Client.File.Url,
            Sha1: info.Logging.Client.File.Sha1,
            Size: (int)info.Logging.Client.File.Size,
            Path: Path.Combine(basePath, "versions", info.ID, info.Logging.Client.File.Id)
        );
    }
    public string? GetLoggingConfigPath()
    {
        if (info.Logging?.Client?.File == null)
            return null;
        return Path.Combine(basePath, "versions", info.ID, info.Logging.Client.File.Id);
    }

    public int GetJavaVersion()
    {
        return info?.JavaVersion?.MajorVersion ?? Tools.ForNullJavaVersion(info.ID);
    }
}

