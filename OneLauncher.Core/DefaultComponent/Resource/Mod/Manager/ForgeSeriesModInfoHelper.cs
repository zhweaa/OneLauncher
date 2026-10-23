using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Manager;
internal class ForgeSeriesModInfoHelper : IModInfoHelper
{
    private static readonly Regex ModIdRegex = new(@"modId\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"version\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex DisplayNameRegex = new(@"displayName\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex DescriptionRegex = new(@"description\s*=\s*'''([^']+)'''", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex LogoFileRegex = new(@"logoFile\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    public async Task<ModInfo?> GetModInfoAsync(string filePath, ZipArchive archive, ZipArchiveEntry configEntry)
    {
        await using Stream stream = configEntry.Open();
        using StreamReader reader = new(stream);
        string content = await reader.ReadToEndAsync();

        var modInfo = new ModInfo
        {
            Id = ModIdRegex.Match(content).Groups[1].Value,
            Version = VersionRegex.Match(content).Groups[1].Value,
            Name = DisplayNameRegex.Match(content).Groups[1].Value,
            Description = DescriptionRegex.Match(content).Groups[1].Value.Trim(),
            IsEnabled = !filePath.EndsWith(".disabled"),
            fileName = Path.GetFileName(filePath)
        };

        if (string.IsNullOrEmpty(modInfo.Name))
            modInfo.Name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(modInfo.Id))
            modInfo.Id = modInfo.Name;

        string logoPath = LogoFileRegex.Match(content).Groups[1].Value;
        modInfo.Icon = await ExtractIconAsync(archive, logoPath);

        return modInfo;
    }

    private async Task<byte[]?> ExtractIconAsync(ZipArchive archive, string iconPath)
    {
        if (string.IsNullOrEmpty(iconPath)) return null;

        ZipArchiveEntry? iconEntry = archive.GetEntry(iconPath) ?? archive.GetEntry($"META-INF/{iconPath}");
        if (iconEntry == null) return null;

        await using Stream iconStream = iconEntry.Open();
        using MemoryStream memoryStream = new();
        await iconStream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }
}