using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OneLauncher.Core.Global;
using OneLauncher.Core.DefaultComponent.Mod.ModManager;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Manager;
internal class FabricModInfoHelper : IModInfoHelper 
{
    public async Task<ModInfo?> GetModInfoAsync(string filePath, ZipArchive archive, ZipArchiveEntry configEntry)
    {
        FabricModJson info = await JsonSerializer.DeserializeAsync<FabricModJson>(
            configEntry.Open(),
            FabricModJsonContext.Default.FabricModJson
        ) ?? throw new OlanException("模组信息解析失败", $"无法从文件'{filePath}'的fabric.mod.json中读取到有效信息");

        byte[]? iconBytes = null;
        if (info.Icon != null)
        {
            // 使用传入的 archive 来提取图标
            iconBytes = await ExtractIconAsync(archive, info.Icon);
        }

        return new ModInfo
        {
            Id = info.Id ?? "未知ID",
            Version = info.Version ?? "未知版本",
            Name = info.Name ?? Path.GetFileNameWithoutExtension(filePath),
            Description = info.Description ?? "无描述",
            Icon = iconBytes,
            IsEnabled = !filePath.EndsWith(".disabled"),
            fileName = Path.GetFileName(filePath)
        };
    }

    private async Task<byte[]?> ExtractIconAsync(ZipArchive archive, string iconPath)
    {
        if (string.IsNullOrEmpty(iconPath)) return null;

        ZipArchiveEntry? iconEntry = archive.GetEntry(iconPath);
        if (iconEntry == null) return null;

        await using Stream iconStream = iconEntry.Open();
        using MemoryStream memoryStream = new();
        await iconStream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }
}
