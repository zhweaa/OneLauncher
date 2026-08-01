using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OneLauncher.Core.Net.Server;

public class ServerEntry(Guid id, string instanceId, ServerInfo serverInfo, string name, string? description)
{
    private static readonly byte[] PngSignature =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    ];

    public Guid Id { get; init; } = id;
    public string InstanceId { get; init; } = instanceId;
    public ServerInfo ServerInfo { get; set; } = serverInfo;
    
    public string Name { get; set; } = name;
    public string? Description { get; set; } = description;
    [JsonIgnore]
    public string IconFileUrl => Path.Combine(Init.BasePath, "customdata", $"{Id}.png");
    [JsonIgnore]
    public string ReadableAddress => Uri.EscapeDataString($"{ServerInfo.Ip}:{ServerInfo.Port}");

    // 以下是预留属性
    [JsonIgnore]
    public uint? PlayersOnline;
    [JsonIgnore]
    public uint? Ping
    {
        get
        {
            try
            {
                using Ping pingSender = new();
                PingReply reply = pingSender.Send(ServerInfo.Ip, 120);
                return reply.Status == IPStatus.Success
                    ? (uint)Math.Min(reply.RoundtripTime, uint.MaxValue)
                    : null;
            }
            catch (PingException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 通过API获取服务器信息，并更新ServerInfo
    /// </summary>
    public async Task GetAASServerInfo()
    {
        #region 检查
        // 地址检查已经迁移到 ServerInfo构造函数中
        HttpResponseMessage response;
        try
        {
            response = await Init.Download.UnityClient.GetAsync(
                $"https://api.mcstatus.io/v2/status/java/{ReadableAddress}");
        }
        catch (HttpRequestException ex)
        {
            throw new OlanException("无法获取服务器信息", "连接服务器状态 API 失败", OlanExceptionAction.Error, ex);
        }
        #endregion

        using (response)
        {
            await using Stream body = await response.Content.ReadAsStreamAsync();
            if (!response.IsSuccessStatusCode)
                throw new OlanException("无法获取服务器信息", "服务器请求失败");

            JsonNode node = (await JsonNode.ParseAsync(body))?.Root
                ?? throw new OlanException("无法获取服务器信息", "响应不是 JSON 对象");

            bool online;
            string? description;
            string? icon;
            try
            {
                online = node["online"]?.GetValue<bool>() ?? false;
                description = node["motd"]?["clean"]?.GetValue<string>();
                icon = node["icon"]?.GetValue<string>();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new OlanException("无法获取服务器信息", "服务器状态 API 返回了无效数据", OlanExceptionAction.Error, ex);
            }

            if (!online)
            {
                throw new OlanException(
                    "服务器离线",
                    $"无法连接到服务器 {ServerInfo.Ip}:{ServerInfo.Port}",
                    OlanExceptionAction.Warning);
            }

            // 先检查是否已经存在配置，讲究一个不变现有原则。
            if (icon != null && !File.Exists(IconFileUrl))
                await SaveServerIcon(icon);
            if (Description == null && description != null)
                Description = description;
        }
    }

    private async Task SaveServerIcon(string iconDataUri)
    {
        const string prefix = "data:image/png;base64,";
        if (!iconDataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new OlanException("无法获取服务器图标", "服务器返回了不支持的图标格式",OlanExceptionAction.Warning);

        byte[] iconBytes;
        try
        {
            iconBytes = Convert.FromBase64String(iconDataUri[prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new OlanException(
                "无法获取服务器图标",
                "服务器返回了无效的 Base64 图标数据",
                OlanExceptionAction.Error,
                ex);
        }

        if (iconBytes.Length > 4 * 1024 * 1024 ||
            !iconBytes.AsSpan().StartsWith(PngSignature))
        {
            throw new OlanException("无法获取服务器图标", "服务器返回的 PNG 图标无效或文件过大");
        }

        string iconDirectory = Path.GetDirectoryName(IconFileUrl)!;
        Directory.CreateDirectory(iconDirectory);

        await File.WriteAllBytesAsync(IconFileUrl, iconBytes);

    }
}
