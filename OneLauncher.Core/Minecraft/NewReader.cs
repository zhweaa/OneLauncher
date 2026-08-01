using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneLauncher.Core.Minecraft;
public interface INewReader
{
    public Task<MinecraftNew> GetCurrentNewsAsync();
    /// <summary>获取下一条新闻</summary>
    public Task<MinecraftNew> GetNextNewsAsync();
    /// <summary>获取上一条新闻</summary>
    public Task<MinecraftNew> GetPreviousNewsAsync();
}
public class MinecraftNewsReader : INewReader
{
    private const string MojangNewsUrl = "https://launchercontent.mojang.com/v2/news.json";
    private const string MojangContentHost = "https://launchercontent.mojang.com";
    private readonly MinecraftNew nullNew = new MinecraftNew(
        "无标题",
        "无内容",
        "https://www.minecraft.net",
        "https://www.minecraft.net/en-us/about-minecraft"
        );

    private readonly HttpClient _httpClient = Init.Download.UnityClient;
    private int _currentIndex = -1; // -1表示从未加载过
    private int _totalNewsCount = -1; // 缓存新闻总数，避免重复解析来获取长度

    public MinecraftNewsReader()
    {
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"OneLauncher/{Init.ApplicationVersoin}");
    }

    private async Task<MinecraftNew> GetNewsByIndexAsync(int index)
    {
        using (Stream stream = await _httpClient.GetStreamAsync(MojangNewsUrl))
        using (JsonDocument document = await JsonDocument.ParseAsync(stream))
        {
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("entries", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array)
            {
                // 如果是首次加载，更新新闻总数
                if (_totalNewsCount == -1)
                {
                    _totalNewsCount = entries.GetArrayLength();
                }

                // 边界检查
                if (index >= 0 && index < _totalNewsCount)
                {
                    JsonElement entry = entries[index]; // 直接通过索引访问，无需遍历！

                    // 从这个单独的JsonElement中提取数据
                    string title = entry.TryGetProperty("title", out var t) ? t.GetString() : "无标题";
                    string text = entry.TryGetProperty("text", out var tx) ? tx.GetString() : "无内容";
                    string readMoreLink = entry.TryGetProperty("readMoreLink", out var r) ? r.GetString() : "https://www.minecraft.net";
                    string imageUrl = "";
                    if (entry.TryGetProperty("newsPageImage", out var img) && img.TryGetProperty("url", out var u))
                    {
                        imageUrl = $"{MojangContentHost}{u.GetString()}";
                    }

                    return new MinecraftNew(title, text, readMoreLink, imageUrl);
                }
            }
        }
        return nullNew;
    }

    /// <summary>
    /// 获取当前新闻。如果是首次加载，则获取第一条。
    /// </summary>
    public Task<MinecraftNew> GetCurrentNewsAsync()
    {
        int targetIndex = (_currentIndex == -1) ? 0 : _currentIndex;
        return FetchAndSetNewsAsync(targetIndex);
    }

    /// <summary>
    /// 获取下一条新闻。
    /// </summary>
    public Task<MinecraftNew> GetNextNewsAsync()
    {
        if (_totalNewsCount == 0) throw new InvalidOperationException("新闻列表为空。");

        // 如果是首次加载，则从第一条开始；否则计算下一条的索引
        int targetIndex = (_currentIndex == -1) ? 0 : (_currentIndex + 1) % _totalNewsCount;
        return FetchAndSetNewsAsync(targetIndex);
    }

    /// <summary>
    /// 获取上一条新闻。
    /// </summary>
    public Task<MinecraftNew> GetPreviousNewsAsync()
    {
        if (_totalNewsCount == 0) throw new InvalidOperationException("新闻列表为空。");

        int targetIndex = (_currentIndex == -1) ? 0 : (_currentIndex - 1 + _totalNewsCount) % _totalNewsCount;
        return FetchAndSetNewsAsync(targetIndex);
    }

    /// <summary>
    /// 内部辅助方法，用于调用核心获取逻辑并更新当前索引。
    /// </summary>
    private async Task<MinecraftNew> FetchAndSetNewsAsync(int index)
    {
        MinecraftNew news = await GetNewsByIndexAsync(index);
        _currentIndex = index; // 仅在成功获取后才更新索引
        return news;
    }
}