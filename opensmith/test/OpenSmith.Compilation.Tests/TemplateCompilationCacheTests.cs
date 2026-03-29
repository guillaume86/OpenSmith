using OpenSmith.Compilation;

namespace OpenSmith.Cli.Tests;

public class TemplateCompilationCacheTests
{
    [Fact]
    public void HashIsDeterministic()
    {
        var cache = new TemplateCompilationCache();
        var sources = new Dictionary<string, string>
        {
            ["A"] = "source A",
            ["B"] = "source B",
        };

        var hash1 = cache.ComputeHash(sources);
        var hash2 = cache.ComputeHash(sources);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashChangesWithPublishFingerprint()
    {
        var cache = new TemplateCompilationCache();
        var sources = new Dictionary<string, string> { ["A"] = "source" };

        var hashWithout = cache.ComputeHash(sources);
        var hashWith = cache.ComputeHash(sources, "some-fingerprint");

        Assert.NotEqual(hashWithout, hashWith);
    }

    [Fact]
    public void DifferentPublishFingerprintsDifferentHashes()
    {
        var cache = new TemplateCompilationCache();
        var sources = new Dictionary<string, string> { ["A"] = "source" };

        var hash1 = cache.ComputeHash(sources, "fingerprint-1");
        var hash2 = cache.ComputeHash(sources, "fingerprint-2");

        Assert.NotEqual(hash1, hash2);
    }
}
