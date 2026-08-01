using Microsoft.Identity.Client;
using OneLauncher.Core.Global;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OneLauncher.Core.Helper.Models;

public enum SortingType
{
    AnTime_OldFront,
    AnTime_NewFront,
    AnVersion_OldFront,
    AnVersion_NewFront,
}
public struct ServerInfo
{
    public ServerInfo(string ip, ushort port)
    {
        string host = ip?.Trim() ?? string.Empty;
        if (host.Length == 0 || port < 0 || port > 65535)
        {
            throw new OlanException("无法获取服务器信息", "服务器地址或端口无效");
        }

        Ip = host;
        Port = port;
    }
    public string Ip { get; init; }
    public ushort Port { get; init; }
}
public struct ModType
{
    public bool IsFabric { get; set; }
    public bool IsNeoForge { get; set; }
    public bool IsForge { get; set; }
    public bool IsQuilt { get; set; }
    public ModEnum ToModEnum()
    {
        if (IsFabric)
            return ModEnum.fabric;
        if (IsNeoForge)
            return ModEnum.neoforge;
        if (IsForge)
            return ModEnum.forge;
        if (IsQuilt) 
            return ModEnum.quilt;

        return ModEnum.none;
    }
    public static bool operator ==(ModType left, ModEnum right)
    {
        if (left.IsFabric && right == ModEnum.fabric)
            return true;
        else if (left.IsNeoForge && right == ModEnum.neoforge)
            return true;
        else if (left.IsForge && right == ModEnum.forge)
            return true;
        else if (left.IsQuilt && right == ModEnum.quilt) 
            return true;
        else
            return false;
    }
    public static bool operator !=(ModType left, ModEnum right)
        => !(left == right);
}
public enum ModEnum
{
    none,
    fabric,
    neoforge,
    forge,
    quilt
}

/// <summary>
/// 描述单个下载项
/// </summary>
/// 
public struct NdDowItem
{
    /// <param ID="Url">下载地址</param>
    /// <param ID="Sha1">SHA1校验码</param>
    /// <param ID="Path">保存地址（含文件名）</param>
    /// <param Name="Size">文件大小（单位字节）</param>
    public NdDowItem(string Url, string Path, int Size, string? Sha1 = null)
    {
        url = Url;
        path = Path;
        if (Sha1 != null)
            sha1 = Sha1;
    }
    public string url;
    public string path;
    public int size;
    public string? sha1;
}
// 不要把他改成结构体，不然会出一些神奇的问题
public class VersionBasicInfo
{
    /// <param ID="Name">版本标识符</param>
    /// <param ID="type">版本类型</param>
    /// <param ID="url">版本文件下载地址</param>
    /// <param ID="time">版本发布时间</param>
    public VersionBasicInfo(string ID, string type, DateTime time, string url)
    {
        this.ID = ID;
        this.type = type;
        this.time = time;
        Url = url;
    }
    // 如果不重写该方法 AutoCompleteBox 会报错
    public override string ToString()
    {
        return ID.ToString();
    }
    public string ID { get; set; }
    public string type { get; set; }
    public DateTime time { get; set; }
    public string Url { get; set; }
}
public enum SystemType
{
    windows,
    osx,
    linux
}