using System.Diagnostics;
using System.Runtime.InteropServices;
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

        // Include RID since different platforms produce different publish outputs
        sb.Append("rid:");
        sb.Append(RuntimeInformation.RuntimeIdentifier);
        sb.Append('\n');

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
    /// resolving relative feed paths to absolute paths so they work from the temp location.
    /// </summary>
    private static void CopyNuGetConfig(string templateDir, string tempDir)
    {
        var dir = Path.GetFullPath(templateDir);
        while (dir != null)
        {
            var configPath = Path.Combine(dir, "nuget.config");
            if (!File.Exists(configPath))
            {
                // Also check NuGet.Config (case variation common on Windows)
                configPath = Path.Combine(dir, "NuGet.Config");
            }

            if (File.Exists(configPath))
            {
                var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
                var xml = System.Xml.Linq.XDocument.Load(configPath);

                // Resolve relative paths in <packageSources> <add value="..." /> to absolute paths
                var sources = xml.Root?.Element("packageSources");
                if (sources != null)
                {
                    foreach (var add in sources.Elements("add"))
                    {
                        var value = add.Attribute("value")?.Value;
                        if (value == null || value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            continue;
                        // Resolve relative path against the original config location
                        var absolutePath = Path.GetFullPath(Path.Combine(configDir, value));
                        add.SetAttributeValue("value", absolutePath);
                    }
                }

                xml.Save(Path.Combine(tempDir, "nuget.config"));
                return;
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
    }

    private void RunDotnetPublish(string projectDir, string outputDir)
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var args = $"publish -o \"{outputDir}\" -c Release --nologo -v minimal -r {rid} --self-contained false";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
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
            var requested = FormatRequestedPackages();
            var logPath = WritePublishLog(args, projectDir, rid, process.ExitCode, stdout, stderr, requested);

            var sb = new StringBuilder();
            sb.Append("dotnet publish failed (exit code ").Append(process.ExitCode).AppendLine(").");
            sb.Append("  Command:     dotnet ").AppendLine(args);
            sb.Append("  Working dir: ").AppendLine(projectDir);
            if (logPath != null)
                sb.Append("  Log file:    ").AppendLine(logPath);
            if (requested != null)
            {
                sb.AppendLine();
                sb.AppendLine("--- packages requested ---");
                sb.AppendLine(requested);
            }
            sb.AppendLine();
            sb.AppendLine("--- stderr ---");
            sb.AppendLine(string.IsNullOrWhiteSpace(stderr) ? "(empty)" : stderr.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("--- stdout ---");
            sb.AppendLine(string.IsNullOrWhiteSpace(stdout) ? "(empty)" : stdout.TrimEnd());
            throw new InvalidOperationException(sb.ToString());
        }
    }

    private string? FormatRequestedPackages()
    {
        if (_nugetDirectives.Count == 0)
            return null;
        var sb = new StringBuilder();
        foreach (var d in _nugetDirectives)
            sb.Append("  ").Append(d.Package).Append(' ').AppendLine(d.Version);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Writes a diagnostic log file capturing the publish invocation and its output.
    /// Returns the log path, or null if writing failed (best-effort — must not mask the real error).
    /// </summary>
    private string? WritePublishLog(string args, string projectDir, string rid, int exitCode, string stdout, string stderr, string? requestedPackages)
    {
        try
        {
            var logDir = Path.Combine(GetOpenSmithBaseDir(), "logs");
            Directory.CreateDirectory(logDir);

            var fingerprintSuffix = string.IsNullOrEmpty(Fingerprint) ? "nofp" : Fingerprint[..8];
            var fileName = $"publish-{DateTime.Now:yyyyMMdd-HHmmss}-{fingerprintSuffix}.log";
            var logPath = Path.Combine(logDir, fileName);

            var sb = new StringBuilder();
            sb.Append("Command:     dotnet ").AppendLine(args);
            sb.Append("Working dir: ").AppendLine(projectDir);
            sb.Append("RID:         ").AppendLine(rid);
            sb.Append("Fingerprint: ").AppendLine(Fingerprint ?? "(none)");
            sb.Append("Exit code:   ").Append(exitCode).AppendLine();
            sb.Append("Timestamp:   ").AppendLine(DateTime.Now.ToString("O"));
            if (requestedPackages != null)
            {
                sb.AppendLine();
                sb.AppendLine("--- packages requested ---");
                sb.AppendLine(requestedPackages);
            }
            sb.AppendLine();
            sb.AppendLine("--- stderr ---");
            sb.AppendLine(stderr);
            sb.AppendLine("--- stdout ---");
            sb.AppendLine(stdout);

            File.WriteAllText(logPath, sb.ToString());
            return logPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Per-OS base directory for OpenSmith state (logs, dependency cache, etc.).
    /// </summary>
    internal static string GetOpenSmithBaseDir()
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

        return Path.Combine(baseDir, "opensmith");
    }

    private static string GetPublishCacheDirectory(string fingerprint)
        => Path.Combine(GetOpenSmithBaseDir(), "deps", fingerprint);
}

public class OpenSmithManifest
{
    public string? Package { get; set; }
    public string? Version { get; set; }
}
