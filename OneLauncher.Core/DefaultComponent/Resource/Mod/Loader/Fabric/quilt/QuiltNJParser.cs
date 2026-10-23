using OneLauncher.Core.DefaultComponent.Mod.ModLoader.fabric;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.Fabric.quilt;
public class QuiltNJParser
{
    public readonly FabricRoot info;
    private readonly string basePath;

    public QuiltNJParser(FabricRoot json, string BasePath)
    {
        basePath = BasePath;
        info = json;
    }
    //public static QuiltNJParser ParserAuto(Stream json, string basePath)
    //{
    //    using JsonDocument document = JsonDocument.Parse(json);

    //    JsonElement firstElement = document.RootElement[0];

    //    var info = JsonSerializer.Deserialize(firstElement.GetRawText(), FabricJsonContext.Default.FabricRoot)
    //        ?? throw new OlanException("内部错误", "无法解析Quilt文本");
    //    return new QuiltNJParser(info, basePath);

    //}
    //public static FabricVJParser ParserUseVersion(Stream json, string basePath, string version)
    //{
    //    using JsonDocument document = JsonDocument.Parse(json);
    //    var element = document.RootElement;
    //    for (int i = 0; i < element.GetArrayLength(); i++)
    //    {
    //        if (element[i].GetProperty("loader").GetProperty("version").GetString() == version)
    //            return new FabricVJParser(
    //                JsonSerializer.Deserialize(element[i].GetRawText(), FabricJsonContext.Default.FabricRoot),
    //                basePath
    //                );
    //    }
    //    throw new OlanException("内部错误", "无法找到对应的Fabric版本");
    //}
    public string GetMainClass()
    {
        return info.LauncherMeta.MainClass.Client;
    }
    public int GetJavaVersion()
    {
        return info.LauncherMeta.MinJavaVersion;
    }
    public List<NdDowItem> GetLibraries()
    {
        const string quiltBaseUrl = "https://maven.quiltmc.org/repository/release/";
        const string fabricBaseUrl = "https://maven.fabricmc.net/"; // [!code ++]
        List<NdDowItem> dowItems = new List<NdDowItem>(info.LauncherMeta.Libraries.Common.Count + 3);

        // 处理通用库 (通常在 quilt launcherMeta 中定义，应使用 quilt 源)
        foreach (var item in info.LauncherMeta.Libraries.Common)
        {
            string[] parts = item.Name.Split(':');
            string groupId = parts[0];
            string artifactId = parts[1];
            string version = parts[2];

            string urlPathSegments = Path.Combine(groupId.Replace('.', Path.DirectorySeparatorChar),
                                                  artifactId,
                                                  version,
                                                  $"{artifactId}-{version}.jar");
            string url = $"{quiltBaseUrl}{urlPathSegments.Replace('\\', '/')}";

            string fullPath = Path.Combine(basePath,
                                                "libraries",
                                                groupId.Replace('.', Path.DirectorySeparatorChar),
                                                artifactId,
                                                version,
                                                $"{artifactId}-{version}.jar");
            dowItems.Add(new NdDowItem(url, fullPath, item.Size, item.Sha1));
        }

        // 统一处理3个核心库：loader, intermediary, hashed
        // 使用一个辅助函数来减少重复代码
        Action<string, string> addCoreLibrary = (mavenName, baseUrl) =>
        {
            var parts = mavenName.Split(':');
            var groupId = parts[0];
            var artifactId = parts[1];
            var version = parts[2];
            var urlPathSegments = Path.Combine(groupId.Replace('.', Path.DirectorySeparatorChar),
                                               artifactId,
                                               version,
                                               $"{artifactId}-{version}.jar");
            var url = $"{baseUrl}{urlPathSegments.Replace('\\', '/')}";
            var fullPath = Path.Combine(basePath, "libraries",
                                        groupId.Replace('.', Path.DirectorySeparatorChar),
                                        artifactId, version, $"{artifactId}-{version}.jar");
            dowItems.Add(new NdDowItem(url, fullPath, 0));
        };

        // 添加 Quilt Loader (来自 Quilt 源)
        addCoreLibrary(info.Loader.DownName, quiltBaseUrl);

        // [!code focus-start]
        // 添加 Intermediary (来自 Fabric 源) - 这是关键的修复
        addCoreLibrary(info.Intermediary.DownName, fabricBaseUrl);
        // [!code focus-end]

        // 添加 Quilt Hashed (来自 Quilt 源)
        addCoreLibrary(info.QuiltHashed.DownName, quiltBaseUrl);

        return dowItems;
    }

    /// <summary>
    /// 获取Quilt模组加载器在启动时需要加载到类路径的库文件。
    /// </summary>
    /// <returns>一个以 "groupId:artifactId" 为键，库文件完整路径为值的字典。</returns>
    public Dictionary<string, string> GetLibrariesForUsing()
    {
        var libraries = new Dictionary<string, string>(info.LauncherMeta.Libraries.Common.Count + 3);

        Action<string> addLibrary = (mavenName) =>
        {
            if (string.IsNullOrEmpty(mavenName)) return;
            var parts = mavenName.Split(':');
            if (parts.Length < 3) return;

            // 关键点：使用 groupId:artifactId 作为唯一的Key
            var libKey = $"{parts[0]}:{parts[1]}";

            string groupId = parts[0];
            string artifactId = parts[1];
            string version = parts[2];

            string fileName = $"{artifactId}-{version}.jar";
            string fullPath = Path.Combine(
                basePath, "libraries",
                groupId.Replace('.', Path.DirectorySeparatorChar),
                artifactId, version, fileName);

            libraries[libKey] = fullPath;
        };

        info.LauncherMeta.Libraries.Common.ForEach(lib => addLibrary(lib.Name));
        addLibrary(info.Loader.DownName);
        addLibrary(info.Intermediary.DownName);
        addLibrary(info.QuiltHashed.DownName);

        return libraries;
    }
}
