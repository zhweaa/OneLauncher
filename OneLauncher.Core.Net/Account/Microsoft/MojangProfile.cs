using OneLauncher.Core.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Net.Account.Microsoft.JsonModels;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OneLauncher.Core.Net.Account.Microsoft;
public struct MojangSkin
{
    public string Skin;
    public bool IsSlimModel;
}
public class MojangProfile : IDisposable
{
    public readonly string uuid;
    public readonly string accessToken;
    public readonly HttpClient httpClient;
    public MojangProfile(UserModel userModel)
    {
        this.uuid = userModel.uuid.ToString();
        this.accessToken = userModel.AccessToken;
        httpClient = new HttpClient();
        // 设置一些请求头
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
    }
    public async Task<MojangSkin> Get()
    {
        string url = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}";
        using (var response = await httpClient.GetStreamAsync(url))
        {
            string SkinUrls = (
                // 解码Json并解码Base64
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                         (await JsonNode.ParseAsync(response))
                         !["properties"]
                         !.AsArray()
                         ![0]
                         !["value"]
                         !.GetValue<string>()
                        )
                    )
                );
            var texture = JsonSerializer.Deserialize<TextureData>(SkinUrls,MojangProfileJsonContext.Default.TextureData);
            // 不加这行代码就会报错，报错内容是null我也不知道为什么
            bool isSlimModel = texture!.Textures.Skin.Metadata?.Model == "slim";
            return new MojangSkin
            {
                Skin = texture!.Textures.Skin.Url,
                IsSlimModel = isSlimModel
            };
        }
    }
    public async Task SetUseLocalFile(MojangSkin skin)
    {
        const string requestUrl = "https://api.minecraftservices.com/minecraft/profile/skins";
        HttpResponseMessage response = null; // 声明 response，以便在 catch 块中访问

        try
        {
            using (var formData = new MultipartFormDataContent())
            {
                // 添加模型参数
                formData.Add(new StringContent(skin.IsSlimModel ? "slim" : "classic"), "variant");
                var imageFilePath = skin.Skin.ToString();
                var fileStream = File.OpenRead(imageFilePath);
                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");

                formData.Add(streamContent, "file", Path.GetFileName(imageFilePath));

                response = await httpClient.PostAsync(requestUrl, formData);
                response.EnsureSuccessStatusCode();

                Debug.WriteLine($"成功上传本地皮肤文件: {imageFilePath}");
                string successResponseContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"API 成功响应: {successResponseContent}");
            }
        }
        catch (HttpRequestException e)
        {
            Debug.WriteLine($"请求失败: {e.Message}");
            if (response != null) // 尝试读取错误响应内容
            {
                try
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"API 错误响应内容: {errorContent}");
                }
                catch (Exception contentEx)
                {
                    Debug.WriteLine($"读取错误响应内容失败: {contentEx.Message}");
                }
            }
            throw; // 重新抛出异常，让上层处理
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"上传皮肤时发生未知错误: {ex.Message}");
            throw; // 重新抛出异常
        }
    }
    public async Task SetUseUrl(MojangSkin skin)
    {
        const string requestUrl = "https://api.minecraftservices.com/minecraft/profile/skins";
        try
        {
            /*
            var payload = new
            {
                url = skin.Skin.ToString(),
                variant = skin.IsSlimModel ? "slim" : "classic"
            };
            string jsonPayload = JsonSerializer.Serialize(payload);
            */
            string jsonPayload = "{"+$"\"url\":\"{skin.Skin.ToString()}\",\"variant\":\"{(skin.IsSlimModel ? "slim" : "classic")}\""+"}";
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // 发送 POST 请求
            var response = await httpClient.PostAsync(requestUrl, content);
            // 这可能代表麻将服务器无法连接到url，这里尝试下载并手动上传
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                try
                {
                    var tempSavePath = Path.GetTempPath();
                    using (var downTask = new Download(httpClient))
                        await downTask.DownloadFile(skin.Skin, tempSavePath,CancellationToken.None);
                    await SetUseLocalFile(new MojangSkin() { Skin = tempSavePath, IsSlimModel = skin.IsSlimModel });
                }
                catch (HttpRequestException e)
                {
                    throw new OlanException("无法上传皮肤", "在再次尝试后依旧捕获到网络错误", OlanExceptionAction.Error, e);
                }
            }
            response.EnsureSuccessStatusCode();

            Debug.WriteLine($"成功通过 URL 更改皮肤: {skin.Skin.ToString()}");
        }
        catch (HttpRequestException e)
        {
            Debug.WriteLine($"请求失败: {e.Message}");
            throw;
        }
    }
    public async Task GetSkinHeadImage()
    {
        // 确保输出目录存在
        var outputPath = Path.Combine(Init.BasePath, "playerdata", "body");
        Directory.CreateDirectory(outputPath);

        // 1. 获取皮肤
        string sessionUrl = $"https://crafatar.com/renders/body/{uuid}";

        // 2. 调用第三方API下载完整的皮肤图片
        Byte[] skinImage;
        try
        {
            var responseMessage = await httpClient.GetAsync(sessionUrl);
            if ((int)responseMessage.StatusCode != 521)
            {
                skinImage = await responseMessage.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(Path.Combine(outputPath, $"{uuid}.png"), skinImage);
            }
            else
            {
                sessionUrl = $"{Init.ConfigManger.Data.OlanSettings.CrafatarUrl}/renders/body/{uuid}";
                skinImage = await httpClient.GetByteArrayAsync(sessionUrl);
                await File.WriteAllBytesAsync(Path.Combine(outputPath, $"{uuid}.png"), skinImage);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new OlanException(
                "网络异常",
                $"无法从服务器下载皮肤图片{Environment.NewLine}点击 忽略 按钮忽略这个错误并使用史蒂夫皮肤图片",
                OlanExceptionAction.Error,
                ex);
        }
    }
    public void Dispose() => httpClient.Dispose();
    public static async Task<bool> IsValidSkinFile(string skinFile)
    {
        try
        {
            const int widthOffset = 16;
            const int heightOffset = 20;

            // 我们只需要读取文件的前 24 个字节就足够了
            byte[] buffer = new byte[24];

            await using (var stream = new FileStream(skinFile, FileMode.Open, FileAccess.Read))
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead < 24)
                {
                    // 文件太小，肯定不是有效的皮肤文件
                    return false;
                }
            }

            // PNG 格式使用大端序（Big-Endian）存储整数
            // 从 buffer 的第 16 位开始读取 4 字节作为宽度
            int width = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(widthOffset));
            // 从 buffer 的第 20 位开始读取 4 字节作为高度
            int height = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(heightOffset));

            // 现在进行和你原来完全一样的尺寸校验
            bool isValidDimension = (width == 64 && (height == 32 || height == 64));

            // 注意：高清皮肤(128x128) 和老式布局(32x64) 的支持可以根据需要添加，
            // 但 64x64 是最标准的。Mojang API 可能对更大尺寸有更严格的要求。
            // 为了安全起见，可以先只允许最常见的尺寸。
            // bool isValidDimension = (width == 64 && (height == 32 || height == 64));

            // 如果你需要支持更多尺寸，可以恢复原来的逻辑：
            // bool isValidDimension = (width == 64 && (height == 32 || height == 64)) ||
            //                         (width == 128 && height == 128) ||
            //                         (width == 32 && height == 64);

            return isValidDimension;
        }
        catch (Exception ex)
        {
            // 任何异常（如文件不是有效的PNG，读取失败等）都意味着文件无效
            Debug.WriteLine($"Skin validation failed: {ex.Message}");
            return false;
        }
    }
}
