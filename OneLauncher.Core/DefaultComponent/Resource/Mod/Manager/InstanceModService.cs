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

public class InstanceModService
{
    private readonly GameData _instance;
    private readonly string _modsPath;

    public InstanceModService(GameData instance)
    {
        _instance = instance;
        _modsPath = Path.Combine(_instance.InstancePath, "mods");
        Directory.CreateDirectory(_modsPath); // 确保mods文件夹存在
    }

    /// <summary>
    /// 获取当前实例的所有Mod列表
    /// </summary>
    public async Task<List<ModInfo>> GetModsAsync()
    {
        string[] modFiles = Directory.GetFiles(_modsPath);

        List<ModInfo> modInfos = new List<ModInfo>(modFiles.Length);
        foreach (var file in modFiles)
        {
            // 未来可以搞一个接口支持不同加载器
            ModInfo? modInfo = await ModInfoGetter.GetModInfoAsync(file);
            modInfos.Add(modInfo ?? new ModInfo
            {
                Id = "未知标识符",
                Version = "未知版本",
                Name = "未知名称",
                Description = "未知描述",
                Icon = null,
                IsEnabled = false,
                fileName = file
            });
        }
        return modInfos;
    }

    /// <summary>
    /// 启用一个Mod
    /// </summary>
    public void EnableModAsync(string modFileName)
    {
        string disabledPath = Path.Combine(_modsPath, modFileName);//$"{modFileName}.disabled"); // 文件名已经包含.disabled后缀
        string enabledPath = Path.Combine(_modsPath,
            string.Join(".", modFileName.Split('.')[..^1]));

        if (File.Exists(disabledPath))
            File.Move(disabledPath, enabledPath);
    }

    /// <summary>
    /// 禁用一个Mod
    /// </summary>
    public void DisableModAsync(string modFileName)
    {
        string enabledPath = Path.Combine(_modsPath, modFileName);
        string disabledPath = Path.Combine(_modsPath, $"{modFileName}.disabled");

        if (File.Exists(enabledPath))
            File.Move(enabledPath, disabledPath);
    }
}
