using System.Security.Cryptography;
using System.Text;

namespace OpenSmith.Compilation;

/// <summary>
/// Caches compiled template assemblies on disk, keyed by SHA256 of the input sources.
/// </summary>
public class TemplateCompilationCache
{
    private readonly string _cacheDir;

    public TemplateCompilationCache()
    {
        _cacheDir = GetCacheDirectory();
        Directory.CreateDirectory(_cacheDir);
    }

    public string ComputeHash(Dictionary<string, string> sources, string? publishFingerprint = null)
    {
        using var sha = SHA256.Create();

        // Mix in the publish fingerprint so dependency changes invalidate cached template DLLs
        if (publishFingerprint != null)
        {
            var fpBytes = Encoding.UTF8.GetBytes(publishFingerprint);
            sha.TransformBlock(fpBytes, 0, fpBytes.Length, null, 0);
        }

        foreach (var (key, value) in sources.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sha.TransformBlock(Encoding.UTF8.GetBytes(key), 0, Encoding.UTF8.GetByteCount(key), null, 0);
            sha.TransformBlock(Encoding.UTF8.GetBytes(value), 0, Encoding.UTF8.GetByteCount(value), null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    public bool TryLoadCached(string hash, out byte[] assemblyBytes)
    {
        var path = GetCachePath(hash);
        if (File.Exists(path))
        {
            assemblyBytes = File.ReadAllBytes(path);
            return true;
        }
        assemblyBytes = [];
        return false;
    }

    public void Store(string hash, byte[] assemblyBytes)
    {
        var path = GetCachePath(hash);
        var tmpPath = path + ".tmp";
        File.WriteAllBytes(tmpPath, assemblyBytes);
        File.Move(tmpPath, path, overwrite: true);
    }

    private string GetCachePath(string hash) => Path.Combine(_cacheDir, hash + ".dll");

    private static string GetCacheDirectory()
    {
        string baseDir;
        if (OperatingSystem.IsLinux())
        {
            baseDir = Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        }
        else if (OperatingSystem.IsMacOS())
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches");
        }
        else
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        return Path.Combine(baseDir, "opensmith", "cache");
    }
}
