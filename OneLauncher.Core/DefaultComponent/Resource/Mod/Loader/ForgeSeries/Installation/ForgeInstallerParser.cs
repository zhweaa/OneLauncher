using System.IO.Compression;
using System.Text.Json;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Processors;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.JarMod;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Launch;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Legacy;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Profiles;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

internal static class ForgeInstallerParser
{
    public static ForgePreparedInstall Parse(byte[] bytes, string modType, string minecraft, string libraries)
    {
        if (modType is not ("Forge" or "NeoForge")) throw new OlanException("安装配置无效", "未知的加载器类型。");
        ForgePaths.Under(Path.GetTempPath(), minecraft);
        using var memory = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var entry = archive.GetEntry("install_profile.json");
        if (entry == null)
        {
            if (archive.GetEntry("mod_MinecraftForge.class") != null) throw JarModSupport.Unsupported();
            throw new OlanException("Forge 安装包损坏", "缺少 install_profile.json。");
        }
        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();
        var profile = JsonSerializer.Deserialize(text, ForgeSeriesJsonContext.Default.ForgeSeriesInstallProfile)
            ?? throw new OlanException("安装配置无效", "无法读取 install_profile.json。");
        using var document = JsonDocument.Parse(text);
        var legacy = profile.Install != null || profile.VersionInfo != null;
        var (version, json, actualMc) = legacy ? LegacyProfileReader.Read(profile, document.RootElement) : ProfileReader.Read(profile, archive);
        if (minecraft != actualMc || (version.InheritsFrom != null && version.InheritsFrom != minecraft) || string.IsNullOrWhiteSpace(version.MainClass))
            throw new OlanException("Forge 版本不匹配", $"请求 Minecraft {minecraft}，安装包声明 {actualMc} / {version.InheritsFrom}。");
        var loader = ModLoaderDetector.Detect(version.MainClass, version.Libraries.Select(l => l.Name),
            version.Arguments == null ? ForgeLaunchArguments.Tokenize(version.MinecraftArguments ?? "") : ForgeLaunchArguments.Expand(version.Arguments.Game));
        if (loader != (modType == "Forge" ? ModEnum.forge : ModEnum.neoforge))
            throw new OlanException("加载器版本不匹配", $"请求 {modType}，安装包声明 {loader}。");
        var prepared = new ForgePreparedInstall { Installer = bytes, Version = version, VersionJson = json,
            Minecraft = minecraft, ModType = modType, Profile = profile, Protocol = legacy ? ForgeInstallProtocol.Legacy : ForgeInstallProtocol.Profile };
        // 零处理器 profile 中未使用的安装工具不是客户端运行依赖。
        var sourceLibraries = version.Libraries.Concat(prepared.Processors.Count == 0 ? [] : profile.Libraries);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new Dictionary<string, ForgeLibraryFile>(comparer);
        var generated = new Dictionary<string, string>(comparer);
        var root = Directory.GetParent(Path.GetFullPath(libraries))!.FullName;
        // 仅解析客户端实际声明的输出；准备阶段不提取资源或写文件。
        var tokens = new ForgeProcessorTokens(prepared, root, Path.Combine(root, "installer.jar"),
            Path.Combine(root, "minecraft.jar"), libraries, false);
        foreach (var processor in prepared.Processors)
            foreach (var output in processor.Outputs)
            {
                var path = Path.GetFullPath(tokens.Resolve(output.Key));
                var hash = tokens.Resolve(output.Value);
                if (generated.TryGetValue(path, out var previousHash) && previousHash != hash)
                    throw new OlanException("Forge 输出冲突", $"同一路径声明了不同输出摘要：{path}");
                generated[path] = hash;
            }
        // 一些旧处理器没有 outputs，但以 PATCHED/PATCHED_SHA 指定最终产物。
        if (prepared.Processors.Any(p => p.Args.Contains("{PATCHED}")) && tokens.Has("PATCHED") && tokens.Has("PATCHED_SHA"))
            generated.TryAdd(tokens.Key("PATCHED"), tokens.Key("PATCHED_SHA"));
        foreach (var library in sourceLibraries.Where(l => l.ClientRequired != false && ForgeLaunchArguments.Allowed(l.Rules)))
        {
            // 显式 path 也不能绕过构件身份校验。
            var mavenPath = ForgePaths.RelativeMaven(library.Name);
            var artifact = library.Downloads?.Artifact;
            var relative = artifact?.Path is { Length: > 0 } ? artifact.Path : mavenPath;
            var target = ForgePaths.Under(libraries, relative);
            string? embedded = null;
            if (legacy && library.Name == profile.Install!.Path) embedded = profile.Install.FilePath;
            else if (archive.GetEntry("maven/" + relative.Replace('\\', '/')) != null) embedded = "maven/" + relative.Replace('\\', '/');
            var url = artifact?.Url;
            var isGenerated = embedded == null && string.IsNullOrWhiteSpace(url) && generated.ContainsKey(target);
            if (embedded == null && !string.IsNullOrWhiteSpace(url) &&
                (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != "https"))
                throw new OlanException("Forge 下载地址无效", $"库地址必须为 HTTPS：{library.Name}");
            if (embedded == null && !isGenerated && artifact != null && string.IsNullOrWhiteSpace(url))
                throw new OlanException("Forge 安装配置无效", $"库 {library.Name} 没有下载来源或内嵌文件。");
            if (artifact == null && embedded == null && !isGenerated)
            {
                var repo = library.Url ?? "https://libraries.minecraft.net/";
                if (!Uri.TryCreate(repo, UriKind.Absolute, out var repository) || repository.Scheme != "https")
                    throw new OlanException("Forge 安装配置无效", $"库仓库地址无效：{repo}");
                url = new Uri(new Uri(repo.TrimEnd('/') + "/"), relative.Replace('\\', '/')).AbsoluteUri;
            }
            var hashes = artifact?.Sha1 is { Length: > 0 } hash ? new[] { hash } : library.Checksums.ToArray();
            if (isGenerated)
            {
                if (hashes.Length > 0 && !hashes.Contains(generated[target], StringComparer.OrdinalIgnoreCase))
                    throw new OlanException("Forge 输出摘要不匹配", $"运行库与处理器声明不同摘要：{library.Name}");
                hashes = [generated[target]];
            }
            var file = new ForgeLibraryFile(library.Name, new NdDowItem(url ?? "", target, artifact?.Size ?? 0,
                hashes.Length == 1 ? hashes[0] : null), hashes, embedded, isGenerated);
            if (embedded != null && archive.GetEntry(ForgePaths.EntryName(embedded)) == null)
                throw new OlanException("Forge 安装包损坏", $"缺少内嵌库 {embedded}。");
            if (paths.TryGetValue(target, out var previous))
            {
                if (!previous.Hashes.SequenceEqual(hashes) || previous.EmbeddedEntry != embedded || previous.Generated != isGenerated)
                    throw new OlanException("Forge 库冲突", $"同一路径声明了不同的构件：{target}");
                continue;
            }
            paths.Add(target, file);
            prepared.Libraries.Add(file);
        }
        return prepared;
    }
}
