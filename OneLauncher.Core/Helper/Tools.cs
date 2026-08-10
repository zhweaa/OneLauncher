using Microsoft.Identity.Client;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OneLauncher.Core.Helper;
public static class Tools
{
    public static void OpenWebsite(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
    public static string ReplacePlaceholders(string template, Dictionary<string, string> placeholders)
    {
        if (string.IsNullOrEmpty(template) || placeholders == null || placeholders.Count == 0)
        {
            return template;
        }

        var sb = new StringBuilder(template);
        foreach (var placeholder in placeholders)
        {
            sb.Replace($"${{{placeholder.Key}}}", placeholder.Value);
        }
        return sb.ToString();
    }
    /// <summary>
    /// 基于主类名的模组加载器判断机制
    /// </summary>
    public static ModEnum MainClassToModEnum(string mainClass)
    {
        return mainClass switch
        {
            "net.minecraftforge.bootstrap.ForgeBootstrap" => ModEnum.forge,
            "cpw.mods.bootstraplauncher.BootstrapLauncher" => ModEnum.neoforge,
            "net.fabricmc.loader.impl.launch.knot.KnotClient" => ModEnum.fabric,
            "org.quiltmc.loader.impl.launch.knot.KnotClient" => ModEnum.quilt,
            "net.minecraft.client.main.Main" => ModEnum.none,
            _ => ModEnum.none
        };
    }
    public static async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken token)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            token.ThrowIfCancellationRequested();
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            await using var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            await using var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, true);
            await sourceStream.CopyToAsync(destStream, token);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            token.ThrowIfCancellationRequested();
            await CopyDirectoryAsync(dir, Path.Combine(destDir, Path.GetFileName(dir)), token);
        }
    }
    /// <summary>
    /// 获取一个当前可用的TCP端口号。
    /// </summary>
    public static int GetFreeTcpPort()
    {
        // 1. 创建一个TCP监听器，并监听0号端口，系统会自动分配一个空闲端口。
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        // 2. 获取分配到的端口号。
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // 3. 立即停止监听，释放该端口，以便我们的核心程序可以使用。
        listener.Stop();

        return port;
    }
    public static async Task<string?> GetFileMD5Async(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            using (var md5 = MD5.Create())
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    byte[] hash = await md5.ComputeHashAsync(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
        catch
        {
            return null;
        }
    }
    public static async Task<IAccount?> UseAccountIDToFind(string accountID)
    {
        return (await Init.MsalAuthenticator.GetCachedAccounts())
            .FirstOrDefault(a => a.HomeAccountId.Identifier == accountID);
    }
    public static void OpenFolder(string folderPath)
    {
        var processOpenInfo = new ProcessStartInfo()
        {
            Arguments = $"\"{folderPath}\"",
            UseShellExecute = true
        };
        Directory.CreateDirectory(folderPath);
        try
        {
            switch (Init.SystemType)
            {
                case SystemType.windows:
                    processOpenInfo.FileName = "explorer.exe";
                    break;
                case SystemType.osx:
                    processOpenInfo.FileName = "open";
                    break;
                case SystemType.linux:
                    processOpenInfo.FileName = "xdg-open";
                    break;
            }
            Process.Start(processOpenInfo);
        }
        catch (Exception ex)
        {
            throw new OlanException(
                "无法打开文件夹",
                "无法执行启动操作",
                OlanExceptionAction.Error);
        }
    }
    public static int ForNullJavaVersion(string version)
    {
        var v = new Version(version);

        // 1.20往上Java21
        if (v >= new Version("1.20"))
            return 21;

        // 1.18 1.19 Java 17
        if (v >= new Version("1.18"))
            return 17;

        // 1.17是Java16
        if (v == new Version("1.17"))
            return 16;

        // 1.16.5及以下都是Java8
        return 8;
    }
    /// <summary>
    /// 把各种奇奇怪怪的仓库坐标转换为标准路径
    /// </summary>
    public static string MavenToPath(string librariesPath, string item)
    {
        if (item.StartsWith("[") && item.EndsWith("]"))
        {
            item = item.Substring(1, item.Length - 2);
        }
        string[] parts = item.Split(':');
        // 包
        string groupId = parts[0];
        // 名
        string artifactId = parts[1];
        // 版本
        string version = parts[2];
        // 可选信息 (classifier)
        string? classifier = null;
        // 后缀名 (extension)
        string suffix = "jar"; // 默认后缀名

        if (parts.Length > 3)
        {
            string more = parts[3];
            if (more.Contains("@"))
            {
                string[] s = more.Split('@');
                classifier = s[0];
                suffix = s[1];
            }
            else
            {
                classifier = more; // 如果没有@，则整个more就是classifier
                // 此时 suffix 保持默认值 "jar"
            }
        }
        else if (version.Contains("@")) // 如果可选信息为空，但版本中包含@，例如 "org.ow2.asm:asm:9.3@jar"
        {
            string[] s = version.Split('@');
            version = s[0];
            suffix = s[1];
        }

        // 构建文件名
        string filename = artifactId + "-" + version;
        if (!string.IsNullOrEmpty(classifier))
        {
            filename += "-" + classifier;
        }
        filename += "." + suffix;

        // 构建完整路径
        string fullPath = Path.Combine(librariesPath,
                                       Path.Combine(groupId.Split('.')),
                                       artifactId,
                                       version,
                                       filename);

        return fullPath;
    }

    /// <summary>
    /// 过滤出 Minecraft 的纯粹正式版版本号（如 1.20, 1.20.6）。
    /// </summary>
    /// <param Name="versions">包含所有 Minecraft 版本名称的列表。</param>
    /// <returns>只包含纯粹正式版版本号的列表。</returns>
    public static List<string> McVsFilter(List<string> versions)
    {
        // 匹配 1.x 历史版本号，以及 24+ 等新的日历版本号（如 26.2, 26.2.1）
        Regex officialVersionRegex = new Regex(@"^(?:1|[2-9][0-9])\.[0-9]{1,2}(?:\.[0-9]{1,3})?$");

        return versions?
            .Where(version => officialVersionRegex.IsMatch(version))
            .ToList() ?? new List<string>();
    }
}
