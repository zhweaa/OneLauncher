using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OneLauncher.Core.Helper.Models;
/// <summary>
/// 游戏数据，基于版本（UserVersion）
/// </summary>
public class GameData
{
    public string Name { get; set; }
    public string VersionId { get; set; }
    public ModEnum ModLoader { get; set; }
    public DateTime CreationTime { get; set; }
    public Guid DefaultUserModelID { get; set; }
    [JsonIgnore]
    public string InstanceId { get; set; }
    // 自定义一些参数
    public JvmArguments? CustomJvmOptimizationArguments { get; set; }
    public string? ExtraJvmArguments { get; set; }
    public string? CustomGameArguments { get; set; }
    [JsonIgnore]
    public string InstancePath => Path.Combine(Init.GameRootPath, "instance", InstanceId);
    [JsonConstructor]
    public GameData(string name, string versionId, ModEnum modLoader, Guid defaultUserModelID,
                    DateTime creationTime,  JvmArguments? customJvmOptimizationArguments,
                    string? extraJvmArguments, string? customGameArguments) 
    {
        Name = name;
        VersionId = versionId;
        ModLoader = modLoader;
        DefaultUserModelID = defaultUserModelID;
        CreationTime = creationTime;
        CustomJvmOptimizationArguments = customJvmOptimizationArguments;
        ExtraJvmArguments = extraJvmArguments;        
        CustomGameArguments = customGameArguments;     
    }
    public GameData(string name, string versionId, ModEnum loader, Guid? userModel)
    {
        Name = name;
        VersionId = versionId;
        ModLoader = loader;
        DefaultUserModelID = userModel ?? Init.AccountManager.GetDefaultUser().UserID;
        CreationTime = DateTime.Now;
        InstanceId = Guid.NewGuid().ToString()[..8]; // 避免路径过长
    }
    public override string ToString()
        => Name;
}
