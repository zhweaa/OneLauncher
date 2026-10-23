using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Global;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Manager;

public class ModInfoGetter
{
    public static async Task<ModInfo?> GetModInfoAsync(string filePath)
    {
        try
        {
            await using FileStream modFileStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            using ZipArchive archive = new(modFileStream, ZipArchiveMode.Read);

            // 1. 尝试作为 Fabric/Quilt 模组处理
            // 使用独立的变量，避免互相干扰
            ZipArchiveEntry? fabricConfig = archive.GetEntry("fabric.mod.json");
            if (fabricConfig != null)
            {
                return await new FabricModInfoHelper().GetModInfoAsync(filePath, archive, fabricConfig);
            }

            // 2. 尝试作为 NeoForge/Forge 模组处理
            ZipArchiveEntry? forgeConfig = archive.GetEntry("META-INF/neoforge.mods.toml")
                                        ?? archive.GetEntry("META-INF/mods.toml");
            if (forgeConfig != null)
            {
                return await new ForgeSeriesModInfoHelper().GetModInfoAsync(filePath, archive, forgeConfig);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}