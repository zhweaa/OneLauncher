using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Manager;
internal interface IModInfoHelper
{
    /// <summary>
    /// 异步从已打开的模组压缩包和指定的配置文件中获取模组信息。
    /// </summary>
    /// <param name="filePath">原始模组文件的完整路径，用于上下文。</param>
    /// <param name="archive">已经打开的模组文件 ZipArchive 实例。</param>
    /// <param name="configEntry">在压缩包中已经定位到的配置文件入口。</param>
    /// <returns>一个包含模组信息的 ModInfo 对象，如果无法解析则抛出异常。</returns>
    Task<ModInfo?> GetModInfoAsync(string filePath, ZipArchive archive, ZipArchiveEntry configEntry);
}
