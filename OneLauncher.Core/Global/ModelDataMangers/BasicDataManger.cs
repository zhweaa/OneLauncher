using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace OneLauncher.Core.Global.ModelDataMangers;

public abstract class BasicDataManager<T> where T : class, new()
{
    public T Data { get; protected set; } = null!;
    public event Action? OnDataChanged; 

    protected string ConfigPath { get; }
    protected virtual bool RequireValidDocument => false;
    private readonly JsonTypeInfo<T> _jsonTypeInfo;
    private readonly SemaphoreSlim _saveLock = new(1, 1); // 防止竞态

    protected BasicDataManager(string configPath)
    {
        ConfigPath = Path.GetFullPath(configPath);
        _jsonTypeInfo = CreateJsonTypeInfo(new JsonSerializerOptions
        {
#if DEBUG
            WriteIndented = true,
#else
            WriteIndented = false,
#endif
        });
    }

    public async Task InitializeAsync(T? initialData = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        if (!File.Exists(ConfigPath))
        {
            Data = initialData ?? new T();
            ValidateData(Data);
            await Save(); 
        }
        else
        {
            Data = await ReadDataAsync();
        }

        // 调用各个子类的特殊初始化方法
        await PostInitialize();
    }

    // Read and validate the manager's own configuration document.
    protected async Task<T> ReadDataAsync()
    {
        try
        {
            await using var stream = File.OpenRead(ConfigPath);
            T value;
            if (stream.Length == 0)
            {
                if (RequireValidDocument) throw new JsonException("配置文件为空。");
                value = new T();
            }
            else
            {
                var deserialized = await JsonSerializer.DeserializeAsync(stream, _jsonTypeInfo);
                if (deserialized == null && RequireValidDocument)
                    throw new JsonException("配置文件不能为 null。");
                value = deserialized ?? new T();
            }
            ValidateData(value);
            return value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new OlanException("加载配置文件失败",
                $"{ConfigPath} 文件无法读取或已损坏。{Environment.NewLine}{ex.Message}",
                OlanExceptionAction.FatalError, ex);
        }
    }

    protected virtual void ValidateData(T data) { }

    protected virtual Task PostInitialize()
        => Task.CompletedTask; // 部分子类无特别操作
    
    public async Task Save()
    {
        await SaveDataAsync(Data);
        NotifyDataChanged();
    }

    // Commit a candidate before publishing it in memory or notifying observers.
    protected async Task SaveDataAsync(T data)
    {
        ValidateData(data);
        await _saveLock.WaitAsync();
        string? temporaryPath = null;
        try
        {
            temporaryPath = ConfigPath + $".{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, data, _jsonTypeInfo);
                await stream.FlushAsync();
            }
            File.Move(temporaryPath, ConfigPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new OlanException("保存配置文件失败", $"无法保存 {ConfigPath}：{ex.Message}",
                OlanExceptionAction.Error, ex);
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"无法清理临时配置文件 {temporaryPath}：{ex.Message}");
                }
            }
            _saveLock.Release();
        }
    }

    protected void NotifyDataChanged()
    {
        // A subscriber failure must not turn a committed save into a failure.
        if (OnDataChanged == null) return;
        foreach (Action subscriber in OnDataChanged.GetInvocationList())
        {
            try { subscriber(); }
            catch (Exception ex) { Trace.TraceError($"配置更改事件处理失败：{ex}"); }
        }
    }
    protected abstract JsonTypeInfo<T> CreateJsonTypeInfo(JsonSerializerOptions options);
}
