using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenSmith.Engine;

namespace OpenSmith.Compilation;

/// <summary>
/// Resolves template dependencies by generating a proxy .csproj and running dotnet publish.
/// The flat publish output directory is used for both Roslyn MetadataReferences and runtime assembly loading.
/// </summary>
public class DependencyPublisher
{
    private readonly string _templateDir;
    private readonly List<NuGetDirective> _nugetDirectives;
    private readonly bool _verbose;

    public string? PublishDirectory { get; private set; }
    public string? Fingerprint { get; private set; }

    public DependencyPublisher(string templateDir, List<NuGetDirective> nugetDirectives, bool verbose = false)
    {
        _templateDir = templateDir;
        _nugetDirectives = nugetDirectives;
        _verbose = verbose;
    }

    /// <summary>
    /// Resolves all dependencies via dotnet publish (or cache hit).
    /// After this call, PublishDirectory points to a flat directory with all managed + native DLLs.
    /// For bare templates with no manifest and no NuGet directives, this is a no-op.
    /// </summary>
    public void Publish()
    {
        var manifest = FindManifest(_templateDir);

        // Bare .cst files with no manifest and no NuGet directives — nothing to resolve
        if (manifest == null && _nugetDirectives.Count == 0)
        {
            PublishDirectory = "";
            Fingerprint = "";
            if (_verbose) Console.WriteLine("  Dependencies: none (bare template)");
            return;
        }

        Fingerprint = ComputeFingerprint(manifest, _nugetDirectives);
        var cacheDir = GetPublishCacheDirectory(Fingerprint);

        // Check cache
        var markerPath = Path.Combine(cacheDir, ".opensmith.marker");
        if (File.Exists(markerPath))
        {
            PublishDirectory = cacheDir;
            if (_verbose) Console.WriteLine($"  Dependencies: cache hit ({Fingerprint[..8]})");
            return;
        }

        if (_verbose) Console.WriteLine($"  Dependencies: resolving via dotnet publish...");

        // Generate proxy project in a temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), "opensmith-deps-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            GenerateProxyProject(tempDir, manifest, _nugetDirectives);
            CopyNuGetConfig(_templateDir, tempDir);

            // Ensure cache output dir exists
            Directory.CreateDirectory(cacheDir);

            RunDotnetPublish(tempDir, cacheDir);

            // Write marker to indicate successful publish
            File.WriteAllText(markerPath, Fingerprint);
            PublishDirectory = cacheDir;
        }
        finally
        {
            // Clean up temp project
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Searches for .opensmith.json manifest by walking up from the template directory.
    /// </summary>
    internal static OpenSmithManifest? FindManifest(string templateDir)
    {
        var dir = Path.GetFullPath(templateDir);
        while (dir != null)
        {
            var manifestPath = Path.Combine(dir, ".opensmith.json");
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                return JsonSerializer.Deserialize<OpenSmithManifest>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    internal static string ComputeFingerprint(OpenSmithManifest? manifest, List<NuGetDirective> nugetDirectives)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();

        // Include manifest package info
        if (manifest != null)
        {
            sb.Append("manifest:");
            sb.Append(manifest.Package ?? "");
            sb.Append(':');
            sb.Append(manifest.Version ?? "");
            sb.Append('\n');
        }

        // Include sorted NuGet directives
        foreach (var ng in nugetDirectives.OrderBy(d => d.Package, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("nuget:");
            sb.Append(ng.Package ?? "");
            sb.Append(':');
            sb.Append(ng.Version ?? "");
            sb.Append('\n');
        }

        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void GenerateProxyProject(string tempDir, OpenSmithManifest? manifest, List<NuGetDirective> nugetDirectives)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<Project Sdk="Microsoft.NET.Sdk">""");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");

        // From manifest: the template package itself
        if (manifest != null && !string.IsNullOrEmpty(manifest.Package))
        {
            var version = manifest.Version ?? "*";
            sb.AppendLine($"""    <PackageReference Include="{manifest.Package}" Version="{version}" />""");
        }

        // From <%@ NuGet %> directives
        foreach (var ng in nugetDirectives)
        {
            if (string.IsNullOrEmpty(ng.Package)) continue;
            var version = ng.Version ?? "*";
            sb.AppendLine($"""    <PackageReference Include="{ng.Package}" Version="{version}" />""");
        }

        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</Project>");

        File.WriteAllText(Path.Combine(tempDir, "Proxy.csproj"), sb.ToString());
    }

    /// <summary>
    /// Copies nuget.config from the template directory (or its ancestors) to the temp directory,
    /// so custom/local NuGet feeds are available during restore.
    /// </summary>
    private static void CopyNuGetConfig(string templateDir, string tempDir)
    {
        var dir = Path.GetFullPath(templateDir);
        while (dir != null)
        {
            var configPath = Path.Combine(dir, "nuget.config");
            if (File.Exists(configPath))
            {
                File.Copy(configPath, Path.Combine(tempDir, "nuget.config"), overwrite: true);
                return;
            }
            // Also check NuGet.Config (case variation common on Windows)
            configPath = Path.Combine(dir, "NuGet.Config");
            if (File.Exists(configPath))
            {
                File.Copy(configPath, Path.Combine(tempDir, "nuget.config"), overwrite: true);
                return;
            }
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
    }

    private void RunDotnetPublish(string projectDir, string outputDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish -o \"{outputDir}\" -c Release --nologo -v quiet",
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var message = $"dotnet publish failed (exit code {process.ExitCode}):\n{stderr}";
            if (_verbose && !string.IsNullOrWhiteSpace(stdout))
                message += $"\n{stdout}";
            throw new InvalidOperationException(message);
        }
    }

    private static string GetPublishCacheDirectory(string fingerprint)
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

        return Path.Combine(baseDir, "opensmith", "deps", fingerprint);
    }
}

public class OpenSmithManifest
{
    public string? Package { get; set; }
    public string? Version { get; set; }
}
