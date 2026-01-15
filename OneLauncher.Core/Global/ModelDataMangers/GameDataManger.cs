using OneLauncher.Core.Helper.Models;
using System.Collections.Generic;
using System.Data;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Sources;

namespace OneLauncher.Core.Global.ModelDataMangers;
// 标签系统太过度设计了，未来把他简化一下
public class GameDataTag
{
    public string Name { get; init; }
    public Guid ID { get; init; }
}
public class GameDataRoot : IJsonOnDeserialized
{
    /// <summary>
    /// 存储所有游戏数据实例的字典。
    /// Key: InstanceId (string)
    /// Value: GameData 对象
    /// </summary>
    [JsonPropertyName("instances")]
    public Dictionary<string, GameData> Instances { get; set; } = new();

    /// <summary>
    /// 存储每个游戏版本的默认实例映射。
    /// Key: VersionId (e.g., "1.20.1")
    /// Value: InstanceId
    /// </summary>
    [JsonPropertyName("defaults")]
    public Dictionary<string, string> DefaultInstanceMap { get; set; } = new();

    /// <summary>
    /// 存储所有已定义的标签列表。
    /// </summary>
    [JsonPropertyName("tags")]
    public Dictionary<Guid,GameDataTag> Tags { get; set; } = new();
    /// <summary>
    /// 实例ID对于标签ID的映射。
    /// </summary>
    [JsonPropertyName("tagMap")]
    public Dictionary<string,Guid> TagMap { get; set; } = new();

    public void OnDeserialized()
    {
        // 修复 Instances 中的 ID
        foreach (var kvp in Instances)
        {
            if (kvp.Value != null)
            {
                kvp.Value.InstanceId = kvp.Key;
            }
        }

        // 如果 Tags 也有同样问题（Tag对象里也有ID），可以在这里一并处理
        //foreach (var kvp in Tags)
        //{
        //    if (kvp.Value != null)
        //    {
        //        // 假设 GameDataTag 也有 [JsonIgnore] public Guid ID {get;init;}
        //        // 注意：init 属性在构造后无法修改，可能需要改为 set 或通过反射/构造函数处理
        //        // 这里仅作逻辑演示
        //    }
        //}
    }
}
// 以便AOT编译
[JsonSerializable(typeof(List<GameData>))]
[JsonSerializable(typeof(GameDataRoot))]
[JsonSerializable(typeof(GameDataTag))]
[JsonSerializable(typeof(GameData))]
[JsonSerializable(typeof(JvmArguments))]
[JsonSerializable(typeof(string))]
public partial class GameDataJsonContext : JsonSerializerContext { }

public class GameDataManager : BasicDataManager<GameDataRoot>
{
    public List<GameData> AllGameData => Data.Instances.Values.ToList();
    /// <summary>
    /// 获取或创建一个指定版本的游戏数据实例。
    /// 查找逻辑：1. 默认实例 -> 2. 第一个可用实例 -> 3. 创建新实例。
    /// </summary>
    public async Task<GameData> GetOrCreateInstanceAsync(UserVersion userVersion)
    {
        // 尝试获取该版本的默认实例
        var gameData = GetDefaultInstance(userVersion.VersionID);
        if (gameData != null)
            return gameData;
        

        // 2. 如果没有默认实例，则查找该版本的第一个可用实例
        gameData = Data.Instances.FirstOrDefault(x => x.Value.VersionId == userVersion.VersionID).Value;
        if (gameData != null)
        {
            await SetDefaultInstanceAsync(gameData.InstanceId);
            return gameData;
        }

        // 如果完全没有任何实例，则创建一个新的
        // 确定默认游戏数据名称
        string modLoaderName = userVersion.modType.ToModEnum() switch
        {
            ModEnum.fabric => "Fabric",
            ModEnum.neoforge => "NeoForge",
            ModEnum.forge => "Forge",
            _ => "原版"
        };
        string gameDataName = $"{userVersion.VersionID} - {modLoaderName}";
        var newGameData = new GameData(
            name: gameDataName,
            versionId: userVersion.VersionID,
            loader: userVersion.modType.ToModEnum(),
            userModel: Init.AccountManager.GetDefaultUser().UserID
        );

        // 添加并设为默认
        await AddGameDataAsync(newGameData);
        await SetDefaultInstanceAsync(newGameData.InstanceId);

        return newGameData;
    }
    public GameData? GetInstanceFromId(string instanceId)
    {
        if (Data.Instances.ContainsKey(instanceId))
        {
            return base.Data.Instances[instanceId];
        }
        else return null;
    }
    public GameDataManager(string configPath)
        :base(configPath)
    {
    }
    public IEnumerable<GameData> GetInstancesFromTag(Guid tagId)
    {
        var result = new List<GameData>();
        // 1. 遍历 TagMap 寻找匹配的实例ID
        foreach (var tagMapEntry in Data.TagMap)
        {
            // 2. 如果标签ID匹配
            if (tagMapEntry.Value == tagId)
            {
                string instanceId = tagMapEntry.Key;

                // 3. 使用实例ID从 Instances 字典中获取 GameData 对象
                if (Data.Instances.TryGetValue(instanceId, out GameData gameData))
                {
                    // 4. 将找到的实例添加到结果列表中
                    result.Add(gameData);
                }
            }
        }

        return result;
    }
    public Task CreateTag(GameDataTag tag, Guid tagId)
    {
        Data.Tags.Add(tagId, tag);
        return Save();
    }
    public Task SetTagForInstance(string instanceId, Guid tagId)
    {
        Data.TagMap[instanceId] = tagId;
        return Save();
    }
    /// <summary>
    /// 移除一个实例的标签引用。
    /// </summary>
    public Task RemoveTagFromInstanceAsync(string instanceId)
    {
        if (Data.TagMap.ContainsKey(instanceId))
        {
            Data.TagMap.Remove(instanceId);
        }
        return Save();
    }
    public Task RemoveTagAsync(Guid tagId)
    {
        if (Data.Tags.ContainsKey(tagId))
        {
            // 找到所有引用此标签的实例ID
            var instancesWithTag = Data.TagMap
                .Where(kvp => kvp.Value == tagId)
                .Select(kvp => kvp.Key)
                .ToList(); // 同样需要 ToList 来避免在遍历时修改集合

            // 移除所有引用
            foreach (var instanceId in instancesWithTag)
            {
                Data.TagMap.Remove(instanceId);
            }

            // 最后移除标签本身
            Data.Tags.Remove(tagId);
        }
        return Save();
    }
    public Task SetDefaultInstanceAsync(string targetId)
    {
        Data.DefaultInstanceMap[Data.Instances[targetId].VersionId] = targetId;
        return Save();
    }
    public GameData? GetDefaultInstance(string versionId)
    {
        var defaultInstanceId = Data.DefaultInstanceMap.GetValueOrDefault(versionId);
        if (defaultInstanceId == null)
            return null;
        return
            Data.Instances.GetValueOrDefault(defaultInstanceId); 
    }
    public Task AddGameDataAsync(GameData newData)
    {
        Data.Instances.Add(newData.InstanceId,newData);
        // 确保物理文件夹被创建
        Directory.CreateDirectory(newData.InstancePath);
        return Save();
    }
    public Task RemoveGameDataAsync(string dataToRemove)
    {
        // 在删除实例前检查并清理它在 defaults 映射中的记录
        if (Data.DefaultInstanceMap.ContainsValue(dataToRemove))
        {
            var entry = Data.DefaultInstanceMap.FirstOrDefault(kvp => kvp.Value == dataToRemove);
            if (!string.IsNullOrEmpty(entry.Key))
                Data.DefaultInstanceMap.Remove(entry.Key);
        }
        Data.Instances.Remove(dataToRemove);
        return Save();
    }

    protected override JsonSerializerContext GetJsonContext()
        => GameDataJsonContext.Default;
}