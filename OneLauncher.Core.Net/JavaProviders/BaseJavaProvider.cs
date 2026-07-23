using OneLauncher.Core.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OneLauncher.Core.Net.JavaProviders;
internal abstract class BaseJavaProvider
{
    protected readonly string systemTypeName;
    protected readonly string systemArchName;
    protected readonly string installTo;
    protected readonly string javaVersion;
    protected readonly HttpClient httpClient = Init.Download.unityClient; // 如果不需求高安全性身份验证就用这个
    public abstract string ProviderName { get; }
    public override string ToString() => ProviderName;
    public CancellationToken? CancelToken { get; set; }
    public BaseJavaProvider(int javaVersion,int? minimumVersionSupport)
    {
        if(minimumVersionSupport != null)
        if(javaVersion < minimumVersionSupport)
            throw new OlanException("不支持的Java版本", $"当前Java版本{javaVersion}不受支持，最低支持版本为{minimumVersionSupport}。");
        systemTypeName = 
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
            throw new OlanException("不支持的操作系统", "当前操作系统不受支持，请使用Windows、Linux或macOS。");
        //systemTypeName = "macos";
        systemArchName = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "aarch64",
            _ => throw new OlanException("不支持的架构", "当前操作系统架构不受支持，请使用x64或aarch64架构的操作系统。")
        };

        this.javaVersion = javaVersion.ToString();
        this.installTo = Path.Combine(Init.InstalledPath, "runtimes", this.javaVersion);
    }
    public virtual string GetJavaPath()
    {
        //string fname = Directory.GetDirectories(installTo)[0];
        //if (Path.GetDirectoryName(fname)!.Contains("zulu"))
        //{
            
        //}
        if (systemTypeName == "macos")
            return Path.Combine(Directory.GetDirectories(installTo)[0], "Contents", "Home", "bin", "java");
        else return Path.Combine(Directory.GetDirectories(installTo)[0], "bin", "java");
    }
    protected async Task GetAndDownloadAsync(Func<Task<string?>> GetDownloadUrlFuntion, IProgress<(long Start, long End)> op)
    {
        string? downloadUrl = await GetDownloadUrlFuntion();
        if (downloadUrl == null)
            throw new OlanException("无法获取Java下载链接", "从Java提供商API获取Java下载链接时出错，可能是API不可用或参数错误。");
        string tempFilePath = Path.Combine(installTo, $"{javaVersion}.tmp");
        CancellationToken cancellationToken = CancelToken ?? CancellationToken.None;
        const int maxArchiveAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await Init.Download.DownloadFileBig(downloadUrl, tempFilePath, null, 28,
                    segmentProgress: op,
                    token: cancellationToken);

                if (downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // 在解压前打开一次归档，尽早发现下载内容损坏并重试。
                    using (ZipFile.OpenRead(tempFilePath)) { }
                    Download.ExtractFile(tempFilePath, installTo);
                }
                // 特殊处理tar.gz格式
                else
                {
                    var tempTarPath = Path.Combine(installTo, $"{javaVersion}.tar");

                    using (FileStream sourceFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (GZipStream gzipStream = new GZipStream(sourceFileStream, CompressionMode.Decompress))
                    using (FileStream tempTarFileStream = new FileStream(tempTarPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        await gzipStream.CopyToAsync(tempTarFileStream, cancellationToken);

                    using (FileStream tarFileStream = new FileStream(tempTarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        await TarFile.ExtractToDirectoryAsync(tarFileStream, installTo, overwriteFiles: true);
                    // 删除额外的临时文件
                    File.Delete(tempTarPath);
                }

                File.Delete(tempFilePath);
                return;
            }
            catch (InvalidDataException) when (attempt < maxArchiveAttempts)
            {
                File.Delete(tempFilePath);
                File.Delete(Path.Combine(installTo, $"{javaVersion}.tar"));
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }
}
