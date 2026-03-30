using System.IO.Compression;
using System.Xml.Linq;

namespace OpenSmith.Sdk.TemplatePackage.Tests;

[CollectionDefinition("TemplatePackage")]
public class TemplatePackageCollection : ICollectionFixture<TemplatePackageFixture>;

public class TemplatePackageFixture : IAsyncLifetime
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static readonly string SdkProjectDir = Path.Combine(
        RepoRoot, "opensmith", "src", "OpenSmith.Sdk.TemplatePackage");

    public string RootTempDir { get; private set; } = null!;
    public string LocalFeedPath { get; private set; } = null!;
    public string NuGetCachePath { get; private set; } = null!;
    public string SdkVersion { get; private set; } = null!;

    public IDictionary<string, string> EnvVars => new Dictionary<string, string>
    {
        ["NUGET_PACKAGES"] = NuGetCachePath,
    };

    public async Task InitializeAsync()
    {
        RootTempDir = Path.Combine(Path.GetTempPath(), $"opensmith-sdk-tests-{Guid.NewGuid():N}");
        LocalFeedPath = Path.Combine(RootTempDir, "local-feed");
        NuGetCachePath = Path.Combine(RootTempDir, "nuget-cache");
        SdkVersion = $"0.0.1-test.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        Directory.CreateDirectory(LocalFeedPath);
        Directory.CreateDirectory(NuGetCachePath);

        var result = await DotNetCliHelper.PackAsync(SdkProjectDir, LocalFeedPath, SdkVersion, EnvVars);
        result.EnsureSuccess();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(RootTempDir))
        {
            try { Directory.Delete(RootTempDir, recursive: true); }
            catch { /* best effort cleanup */ }
        }
        return Task.CompletedTask;
    }

    public string CreateTestDir(string testName)
    {
        var dir = Path.Combine(RootTempDir, testName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void WriteNuGetConfig(string dir, params string[] extraPackagePatterns)
    {
        var patterns = new List<string> { "OpenSmith.Sdk.*" };
        patterns.AddRange(extraPackagePatterns);

        var patternLines = string.Join("\n      ",
            patterns.Select(p => $"<package pattern=\"{p}\" />"));

        File.WriteAllText(Path.Combine(dir, "nuget.config"),
            $"""
             <?xml version="1.0" encoding="utf-8"?>
             <configuration>
               <packageSources>
                 <clear />
                 <add key="test-local" value="{LocalFeedPath}" />
                 <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
               </packageSources>
               <packageSourceMapping>
                 <packageSource key="test-local">
                   {patternLines}
                 </packageSource>
                 <packageSource key="nuget.org">
                   <package pattern="*" />
                 </packageSource>
               </packageSourceMapping>
             </configuration>
             """);
    }

    /// <summary>
    /// Creates a minimal template package project that references OpenSmith.Sdk.TemplatePackage.
    /// </summary>
    public string CreateTemplateProject(
        string parentDir,
        string packageId,
        Dictionary<string, string> templates,
        string? customTargetsContent = null,
        string? customPropsContent = null)
    {
        var projectDir = Path.Combine(parentDir, packageId);
        Directory.CreateDirectory(projectDir);

        // Write templates
        foreach (var (relativePath, content) in templates)
        {
            var fullPath = Path.Combine(projectDir, "Templates", relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        // Write custom build files if provided
        if (customTargetsContent is not null)
        {
            var buildDir = Path.Combine(projectDir, "build");
            Directory.CreateDirectory(buildDir);
            File.WriteAllText(Path.Combine(buildDir, $"{packageId}.targets"), customTargetsContent);
        }

        if (customPropsContent is not null)
        {
            var buildDir = Path.Combine(projectDir, "build");
            Directory.CreateDirectory(buildDir);
            File.WriteAllText(Path.Combine(buildDir, $"{packageId}.props"), customPropsContent);
        }

        // Write csproj
        File.WriteAllText(Path.Combine(projectDir, $"{packageId}.csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>netstandard2.0</TargetFramework>
                 <LangVersion>latest</LangVersion>
                 <PackageId>{packageId}</PackageId>
                 <Version>{SdkVersion}</Version>
               </PropertyGroup>
               <ItemGroup>
                 <PackageReference Include="OpenSmith.Sdk.TemplatePackage"
                                   Version="{SdkVersion}" PrivateAssets="all" />
               </ItemGroup>
             </Project>
             """);

        WriteNuGetConfig(projectDir, $"{packageId}");

        return projectDir;
    }

    /// <summary>
    /// Creates a minimal consumer project that references a template package.
    /// </summary>
    public string CreateConsumerProject(
        string parentDir,
        string consumerName,
        string templatePackageId,
        string templatePackageVersion,
        string feedPath)
    {
        var projectDir = Path.Combine(parentDir, consumerName);
        Directory.CreateDirectory(projectDir);

        File.WriteAllText(Path.Combine(projectDir, $"{consumerName}.csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>net10.0</TargetFramework>
               </PropertyGroup>
               <ItemGroup>
                 <PackageReference Include="{templatePackageId}" Version="{templatePackageVersion}" />
               </ItemGroup>
             </Project>
             """);

        File.WriteAllText(Path.Combine(projectDir, "Placeholder.cs"),
            "namespace ConsumerApp; public class Placeholder { }");

        // NuGet config pointing to the provided feed (which has both SDK and template packages)
        var patterns = string.Join("\n      ",
            new[] { "OpenSmith.Sdk.*", templatePackageId }
                .Select(p => $"<package pattern=\"{p}\" />"));

        File.WriteAllText(Path.Combine(projectDir, "nuget.config"),
            $"""
             <?xml version="1.0" encoding="utf-8"?>
             <configuration>
               <packageSources>
                 <clear />
                 <add key="test-local" value="{feedPath}" />
                 <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
               </packageSources>
               <packageSourceMapping>
                 <packageSource key="test-local">
                   {patterns}
                 </packageSource>
                 <packageSource key="nuget.org">
                   <package pattern="*" />
                 </packageSource>
               </packageSourceMapping>
             </configuration>
             """);

        return projectDir;
    }

    /// <summary>
    /// Packs a template project and returns the path to the .nupkg file.
    /// </summary>
    public async Task<string> PackTemplateProjectAsync(string projectDir)
    {
        var result = await DotNetCliHelper.PackAsync(projectDir, LocalFeedPath, SdkVersion, EnvVars);
        result.EnsureSuccess();

        var nupkgs = Directory.GetFiles(LocalFeedPath, "*.nupkg")
            .Where(f => !f.Contains("OpenSmith.Sdk.TemplatePackage"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        return nupkgs.First();
    }

    /// <summary>
    /// Extracts a nupkg and returns the list of entry paths.
    /// </summary>
    public static HashSet<string> GetNupkgEntries(string nupkgPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        return archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a file from inside a nupkg.
    /// </summary>
    public static string ReadNupkgEntry(string nupkgPath, string entryPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        var entry = archive.GetEntry(entryPath)
            ?? throw new FileNotFoundException($"Entry '{entryPath}' not found in {nupkgPath}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads nuspec dependency package IDs from inside a nupkg.
    /// </summary>
    public static HashSet<string> GetNuspecDependencies(string nupkgPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("No .nuspec found in " + nupkgPath);

        using var stream = nuspecEntry.Open();
        var doc = XDocument.Load(stream);
        var ns = doc.Root!.Name.Namespace;

        return doc.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")?.Value ?? "")
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
