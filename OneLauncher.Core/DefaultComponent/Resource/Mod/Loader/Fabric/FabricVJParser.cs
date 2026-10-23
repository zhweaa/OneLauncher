using OneLauncher.Core.DefaultComponent.Mod.ModLoader.fabric;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System.Text.Json;
namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.Fabric;

public class FabricVJParser
{
    public readonly FabricRoot info;
    private readonly string basePath;

    public FabricVJParser(FabricRoot json, string BasePath)
    {
        basePath = BasePath;
        info = json;
    }
    //public static FabricVJParser ParserAuto(Stream json,string basePath)
    //{
    //    using JsonDocument document = JsonDocument.Parse(json);
        
    //    JsonElement firstElement = document.RootElement[0];

    //    var info = JsonSerializer.Deserialize(firstElement.GetRawText(), FabricJsonContext.Default.FabricRoot)
    //        ?? throw new OlanException("内部错误","无法解析Fabric文本");
    //    return new FabricVJParser(info, basePath);  
        
    //}
    //public static FabricVJParser ParserUseVersion(Stream json, string basePath,string version)
    //{
    //    using JsonDocument document = JsonDocument.Parse(json);
    //    var element = document.RootElement;
    //    for(int i = 0;i < element.GetArrayLength();i++)
    //    {
    //        if (element[i].GetProperty("loader").GetProperty("version").GetString() == version)
    //            return new FabricVJParser(
    //                JsonSerializer.Deserialize(element[i].GetRawText(),FabricJsonContext.Default.FabricRoot),
    //                basePath
    //                );
    //    }
    //    throw new OlanException("内部错误","无法找到对应的Fabric版本");
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
        const string defaultBaseUrl = "https://maven.fabricmc.net/";
        List<NdDowItem> dowItems = new List<NdDowItem>(info.LauncherMeta.Libraries.Common.Count+2);
        foreach (var item in info.LauncherMeta.Libraries.Common)
        {
            string[] parts = item.Name.Split(':');
            // 包
            string groupId = parts[0];
            // 名
            string artifactId = parts[1];
            // 版本
            string version = parts[2];
            // 后缀
            string? suffix = parts.Length > 3 ? parts[3] : null;

            // 构造 Url
            // org.ow2.asm:asm:9.8 -> org/ow2/asm/asm/9.8/asm-9.8.jar
            string urlPathSegments = Path.Combine(groupId.Replace('.', Path.DirectorySeparatorChar),
                                                  artifactId,
                                                  version,
                                                  $"{artifactId}-{version}.jar");
            string url = $"{defaultBaseUrl}{urlPathSegments.Replace('\\', '/')}"; // 确保是正斜杠

            // 构造 Path
            // Path.Combine(basePath,"libraries", "org","ow2","asm","asm","9.8","asm-9.8.jar");
            string fullPath = Path.Combine(basePath,
                                                "libraries",
                                                groupId.Replace('.', Path.DirectorySeparatorChar),
                                                artifactId,
                                                version,
                                                $"{artifactId}-{version}.jar");
            dowItems.Add(new NdDowItem(url, fullPath, item.Size, item.Sha1));
        }
        // 额外添加两个特殊的
        string[] _parts;
        _parts = info.Loader.DownName.Split(':');
        string _groupId = _parts[0], _artifactId = _parts[1], _version = _parts[2];
        string _urlPathSegments = Path.Combine(_groupId.Replace('.', Path.DirectorySeparatorChar),
                                                  _artifactId,
                                                  _version,
                                                  $"{_artifactId}-{_version}.jar");
        string _url = $"{defaultBaseUrl}{_urlPathSegments.Replace('\\', '/')}"; // 确保是正斜杠
        string _fullPath = Path.Combine(basePath,
                                            "libraries",
                                            _groupId.Replace('.', Path.DirectorySeparatorChar),
                                            _artifactId,
                                            _version,
                                            $"{_artifactId}-{_version}.jar");
        dowItems.Add(new NdDowItem(_url, _fullPath, 0));

        _parts = info.Intermediary.DownName.Split(':');
        _groupId = _parts[0]; _artifactId = _parts[1]; _version = _parts[2];
        _urlPathSegments = Path.Combine(_groupId.Replace('.', Path.DirectorySeparatorChar),
                                                  _artifactId,
                                                  _version,
                                                  $"{_artifactId}-{_version}.jar");
        _url = $"{defaultBaseUrl}{_urlPathSegments.Replace('\\', '/')}"; // 确保是正斜杠
        _fullPath = Path.Combine(basePath,
                                            "libraries",
                                            _groupId.Replace('.', Path.DirectorySeparatorChar),
                                            _artifactId,
                                            _version,
                                            $"{_artifactId}-{_version}.jar");
        dowItems.Add(new NdDowItem(_url, _fullPath, 0));
        return dowItems;
    }
    /// <summary>
    /// 获取Fabric模组加载器在启动时需要加载到类路径的库文件。
    /// </summary>
    /// <returns>一个以 "groupId:artifactId" 为键，库文件完整路径为值的字典。</returns>
    public Dictionary<string, string> GetLibrariesForUsing()
    {
        var libraries = new Dictionary<string, string>(info.LauncherMeta.Libraries.Common.Count + 2);

        Action<string> addLibrary = (mavenName) =>
        {
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

            // Mod库应该总是被加载，使用索引器可以实现覆盖（如果需要）
            libraries[libKey] = fullPath;
        };

        info.LauncherMeta.Libraries.Common.ForEach(lib => addLibrary(lib.Name));
        addLibrary(info.Loader.DownName);
        addLibrary(info.Intermediary.DownName);

        return libraries;
    }
}
