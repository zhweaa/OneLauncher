using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Net.JavaProviders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Global;
public enum JavaProvider
{
    Adoptium,
    AzulZulu,
    MicrosoftOpenJDK,
    OracleGraalVM,
    OracleJDK
}
public class JavaManager
{
    private readonly DBManager _dbManager = Init.ConfigManager;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _installLocks = new();

    public bool IsJavaInstalled(int version) =>
        _dbManager.Data.AvailableJavas.ContainsKey(version);

    public IReadOnlyCollection<int> GetInstalledVersions() =>
        _dbManager.Data.AvailableJavas.Keys.Order().ToArray();

    /// <summary>
    /// 解析指定Java版本的可执行文件路径。
    /// </summary>
    public string GetJavaExecutablePath(int version)
    {
        if (_dbManager.Data.AvailableJavas.TryGetValue(version, out var path) && !string.IsNullOrEmpty(path))
        {
            // 使用通用的占位符替换工具
            var placeholders = new Dictionary<string, string>
            {
                { "INSTALLED_PATH", Init.InstalledPath }
            };
            return Tools.ReplacePlaceholders(path, placeholders).Replace('/', Path.DirectorySeparatorChar);
        }
        return "java";
    }

    /// <summary>
    /// 使用指定的提供商安装一个Java版本。
    /// </summary>
    public async Task InstallJava(
        int version,
        JavaProvider provider,
        bool overwrite = false,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        SemaphoreSlim installLock = _installLocks.GetOrAdd(version, _ => new SemaphoreSlim(1, 1));
        await installLock.WaitAsync(token);
        try
        {
            if (_dbManager.Data.AvailableJavas.ContainsKey(version) && !overwrite)
                return;
            // 以前我是这么写的
            //throw new OlanException("Java版本已存在", $"Java版本 {version} 已经存在于可用列表中。请使用 `overwrite` 参数来覆盖现有版本。",OlanExceptionAction.Warning);

            IJavaProvider javaProvider = provider switch
            {
                JavaProvider.Adoptium => new AdoptiumAPI(version),
                JavaProvider.AzulZulu => new AzulZuluAPI(version),
                JavaProvider.MicrosoftOpenJDK => new MicrosoftBuildofOpenJDKGetter(version),
                JavaProvider.OracleGraalVM => new GraalVMGetter(version),
                JavaProvider.OracleJDK => new OracleJDK(version),
                _ => throw new OlanException("内部错误","不支持的Java提供商。")
            };

            javaProvider.CancelToken = token;

            string installDir = Path.Combine(Init.InstalledPath, "runtimes", version.ToString());
            bool hadExistingConfiguration = _dbManager.Data.AvailableJavas.ContainsKey(version);

            // 如果覆写删除之前目录，避免出现奇奇怪怪的问题
            if (Directory.Exists(installDir) && overwrite) Directory.Delete(installDir, true);
            Directory.CreateDirectory(installDir);

            try
            {
                int done = 0 ;
                await javaProvider.GetAutoAsync(new Progress<(long start,long end)>(p =>
                {
                    // 内部是28分段下载
                    Interlocked.Increment(ref done);
                    progress?.Report($"正在下载 Java：{done}/28 个分段");
                }));

                string javaPath = javaProvider.GetJavaPath();
                string relativePath = Path.GetRelativePath(Init.InstalledPath, javaPath);
                // 保存时，我们依然使用占位符格式
                string placeholderPath = $"${{INSTALLED_PATH}}/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";

                // Java 大版本是配置键，因此每个版本始终只有一条记录。
                _dbManager.Data.AvailableJavas[version] = placeholderPath;
                await _dbManager.Save();
            }
            catch (Exception)
            {
                if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
                if (overwrite && hadExistingConfiguration)
                {
                    _dbManager.Data.AvailableJavas.Remove(version);
                    await _dbManager.Save();
                }
                throw;
            }
        }
        finally
        {
            installLock.Release();
        }
    }
}
