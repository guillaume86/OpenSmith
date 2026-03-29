using OpenSmith.Compilation;
using OpenSmith.Engine;

namespace OpenSmith.Cli.Tests;

public class DependencyPublisherTests
{
    private readonly string _fixtureDir;

    public DependencyPublisherTests()
    {
        _fixtureDir = Path.Combine(Path.GetTempPath(), "OpenSmithDepTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public void FindsManifestInTemplateDirectory()
    {
        File.WriteAllText(Path.Combine(_fixtureDir, ".opensmith.json"),
            """{ "package": "OpenSmith.Plinqo", "version": "1.0.0" }""");

        var manifest = DependencyPublisher.FindManifest(_fixtureDir);

        Assert.NotNull(manifest);
        Assert.Equal("OpenSmith.Plinqo", manifest.Package);
        Assert.Equal("1.0.0", manifest.Version);
    }

    [Fact]
    public void FindsManifestInParentDirectory()
    {
        var subDir = Path.Combine(_fixtureDir, "Internal");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(_fixtureDir, ".opensmith.json"),
            """{ "package": "OpenSmith.Plinqo", "version": "2.0.0" }""");

        var manifest = DependencyPublisher.FindManifest(subDir);

        Assert.NotNull(manifest);
        Assert.Equal("OpenSmith.Plinqo", manifest.Package);
        Assert.Equal("2.0.0", manifest.Version);
    }

    [Fact]
    public void ReturnsNullWhenNoManifestFound()
    {
        var manifest = DependencyPublisher.FindManifest(_fixtureDir);
        Assert.Null(manifest);
    }

    [Fact]
    public void BareTemplate_SkipsPublish()
    {
        var publisher = new DependencyPublisher(_fixtureDir, []);
        publisher.Publish();

        Assert.Equal("", publisher.PublishDirectory);
        Assert.Equal("", publisher.Fingerprint);
    }

    [Fact]
    public void FingerprintIsDeterministic()
    {
        var manifest = new OpenSmithManifest { Package = "Foo", Version = "1.0.0" };
        var directives = new List<NuGetDirective>
        {
            new() { Package = "Bar", Version = "2.0.0" },
        };

        var fp1 = DependencyPublisher.ComputeFingerprint(manifest, directives);
        var fp2 = DependencyPublisher.ComputeFingerprint(manifest, directives);

        Assert.Equal(fp1, fp2);
        Assert.Equal(64, fp1.Length); // SHA256 hex
    }

    [Fact]
    public void FingerprintChangesWithDifferentInputs()
    {
        var manifest1 = new OpenSmithManifest { Package = "Foo", Version = "1.0.0" };
        var manifest2 = new OpenSmithManifest { Package = "Foo", Version = "2.0.0" };
        var empty = new List<NuGetDirective>();

        var fp1 = DependencyPublisher.ComputeFingerprint(manifest1, empty);
        var fp2 = DependencyPublisher.ComputeFingerprint(manifest2, empty);

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void FingerprintIsSortOrderIndependent()
    {
        var directives1 = new List<NuGetDirective>
        {
            new() { Package = "Alpha", Version = "1.0" },
            new() { Package = "Beta", Version = "2.0" },
        };
        var directives2 = new List<NuGetDirective>
        {
            new() { Package = "Beta", Version = "2.0" },
            new() { Package = "Alpha", Version = "1.0" },
        };

        var fp1 = DependencyPublisher.ComputeFingerprint(null, directives1);
        var fp2 = DependencyPublisher.ComputeFingerprint(null, directives2);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void FingerprintDiffersWithAndWithoutManifest()
    {
        var manifest = new OpenSmithManifest { Package = "Foo", Version = "1.0.0" };
        var empty = new List<NuGetDirective>();

        var fpWith = DependencyPublisher.ComputeFingerprint(manifest, empty);
        var fpWithout = DependencyPublisher.ComputeFingerprint(null, empty);

        Assert.NotEqual(fpWith, fpWithout);
    }
}
