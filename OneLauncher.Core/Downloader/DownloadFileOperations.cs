using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.Downloader;

internal static class DownloadFileOperations
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PathLock> PathLocks = new(PathComparer);

    internal static List<NdDowItem> Deduplicate(IEnumerable<NdDowItem> items) =>
        items.DistinctBy(item => Path.GetFullPath(item.path), PathComparer).ToList();

    internal static string CreateTemporaryPath(string targetPath) =>
        $"{targetPath}.{Guid.NewGuid():N}.downloading";

    internal static async Task<IDisposable> AcquireAsync(string targetPath, CancellationToken token)
    {
        string key = Path.GetFullPath(targetPath);
        PathLock pathLock;
        lock (SyncRoot)
        {
            if (!PathLocks.TryGetValue(key, out pathLock!))
            {
                pathLock = new PathLock();
                PathLocks.Add(key, pathLock);
            }
            pathLock.Users++;
        }

        try
        {
            await pathLock.Semaphore.WaitAsync(token);
            return new PathLease(key, pathLock);
        }
        catch
        {
            Release(key, pathLock, acquired: false);
            throw;
        }
    }

    private static void Release(string key, PathLock pathLock, bool acquired)
    {
        lock (SyncRoot)
        {
            if (acquired)
                pathLock.Semaphore.Release();

            // 等待者也持有引用，最后一个使用者退出后才移除并释放锁。
            if (--pathLock.Users == 0)
            {
                PathLocks.Remove(key);
                pathLock.Semaphore.Dispose();
            }
        }
    }

    private sealed class PathLock
    {
        internal readonly SemaphoreSlim Semaphore = new(1, 1);
        internal int Users;
    }

    private sealed class PathLease(string key, PathLock pathLock) : IDisposable
    {
        private PathLock? heldLock = pathLock;

        public void Dispose()
        {
            PathLock? releasedLock = Interlocked.Exchange(ref heldLock, null);
            if (releasedLock != null)
                Release(key, releasedLock, acquired: true);
        }
    }
}
