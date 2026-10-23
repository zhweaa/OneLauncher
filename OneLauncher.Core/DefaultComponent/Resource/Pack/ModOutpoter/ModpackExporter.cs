using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Global;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Resource.Pack;

namespace OneLauncher.Core.DefaultComponent.Resource.Pack.ModOutpoter;

/// <summary>
/// 将启动器中的游戏实例导出为 Modrinth .mrpack 文件。
/// 实例文件写入压缩包的 overrides 目录，因此导入时无需下载清单中的文件。
/// </summary>
public static class ModpackExporter
{
    private const string ManifestFileName = "modrinth.index.json";

    public static async Task<string> ExportAsync(
        GameData gameData,
        IProgress<(int TotalFiles, int CompletedFiles, string FileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameData);

        string instancePath = Path.GetFullPath(gameData.InstancePath);
        if (!Directory.Exists(instancePath))
            throw new OlanException("导出失败", $"实例根目录不存在：{instancePath}", OlanExceptionAction.Error);

        string outputPath = Path.Combine(instancePath, $"{GetSafeFileName(gameData.Name, gameData.InstanceId)}.mrpack");
        // Keep the temporary file beside the destination so the final move remains atomic
        // even when the game root and the system temp directory are on different volumes.
        string temporaryPath = Path.Combine(instancePath, $".olan-export-{Guid.NewGuid():N}.tmp");

        try
        {
            var sourceFiles = Directory.EnumerateFiles(instancePath, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(temporaryPath), StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(Path.GetRelativePath(instancePath, path), ManifestFileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var manifest = CreateManifest(gameData);
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                MrpackJsonContext.Default.ModrinthManifest);

            int totalFiles = sourceFiles.Length + 1;
            int completedFiles = 0;

            await using (var archiveFile = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(archiveFile, ZipArchiveMode.Create, leaveOpen: false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifestEntry = archive.CreateEntry(ManifestFileName, CompressionLevel.Optimal);
                await using (var manifestStream = manifestEntry.Open())
                {
                    await manifestStream.WriteAsync(manifestBytes, cancellationToken);
                }

                completedFiles++;
                progress?.Report((totalFiles, completedFiles, ManifestFileName));

                foreach (string sourceFile in sourceFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relativePath = Path.GetRelativePath(instancePath, sourceFile)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');

                    string packPath = $"overrides/{relativePath}";
                    var entry = archive.CreateEntry(packPath, CompressionLevel.Optimal);
                    await using var sourceStream = new FileStream(
                        sourceFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 128 * 1024,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var entryStream = entry.Open();
                    await sourceStream.CopyToAsync(entryStream, 128 * 1024, cancellationToken);

                    completedFiles++;
                    progress?.Report((totalFiles, completedFiles, packPath));
                }
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            return outputPath;
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw new OlanException("导出失败", $"无法创建整合包：{ex.Message}", OlanExceptionAction.Error, ex);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static ModrinthManifest CreateManifest(GameData gameData)
    {
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["minecraft"] = gameData.VersionId
        };

        string? loaderName = gameData.ModLoader switch
        {
            ModEnum.fabric => "fabric-loader",
            ModEnum.neoforge => "neoforge",
            ModEnum.forge => "forge",
            ModEnum.quilt => "quilt-loader",
            _ => null
        };

        if (loaderName != null)
        {
            dependencies[loaderName] = GetLoaderVersion(gameData);
        }

        return new ModrinthManifest
        {
            FormatVersion = 1,
            Game = "minecraft",
            VersionId = gameData.InstanceId,
            Name = gameData.Name,
            Dependencies = dependencies,
            Files = null
        };
    }

    private static string GetLoaderVersion(GameData gameData)
    {
        string metadataName = gameData.ModLoader switch
        {
            ModEnum.fabric => "version.fabric.json",
            ModEnum.neoforge => "version.neoforge.json",
            ModEnum.forge => "version.forge.json",
            ModEnum.quilt => "version.quilt.json",
            _ => throw new InvalidOperationException("原版实例不包含加载器版本。")
        };
        string metadataPath = Path.Combine(Init.GameRootPath, "versions", gameData.VersionId, metadataName);
        if (!File.Exists(metadataPath))
            throw new OlanException("导出失败", $"找不到加载器元数据：{metadataPath}", OlanExceptionAction.Error);

        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
        if (metadata.RootElement.TryGetProperty("loader", out JsonElement loader) &&
            loader.TryGetProperty("version", out JsonElement loaderVersion) &&
            loaderVersion.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(loaderVersion.GetString()))
            return loaderVersion.GetString()!;

        if (metadata.RootElement.TryGetProperty("id", out JsonElement id) &&
            id.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(id.GetString()))
            return id.GetString()!;

        throw new OlanException("导出失败", $"无法从加载器元数据中确定版本：{metadataPath}", OlanExceptionAction.Error);
    }

    private static string GetSafeFileName(string? name, string? instanceId)
    {
        string fallback = string.IsNullOrWhiteSpace(instanceId) ? "instance" : instanceId;
        string candidate = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(candidate.Length);
        foreach (char character in candidate)
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);

        candidate = builder.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Preserve the original export error.
        }
    }
}
