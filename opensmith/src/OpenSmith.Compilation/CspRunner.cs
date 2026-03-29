using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using OpenSmith.Engine;

namespace OpenSmith.Compilation;

/// <summary>
/// Orchestrates CSP project execution: parse, compile templates, set properties, generate.
/// </summary>
public class CspRunner
{
    private readonly bool _verbose;
    private readonly TemplateCompilationCache? _cache;

    public CspRunner(bool verbose = false, bool useCache = true)
    {
        _verbose = verbose;
        _cache = useCache ? new TemplateCompilationCache() : null;
    }

    public void Run(string cspPath)
    {
        var absoluteCspPath = Path.GetFullPath(cspPath);
        var cspDir = Path.GetDirectoryName(absoluteCspPath)!;
        var xml = File.ReadAllText(absoluteCspPath);
        var project = CspParser.Parse(xml);

        Log($"Parsed CSP: {project.PropertySets.Count} property set(s)");

        // Save and restore working directory per property set
        var originalDir = Directory.GetCurrentDirectory();

        // Resolve assembly probe directories from consumer project's NuGet dependency graph
        var (probePaths, nativeProbePaths) = ResolveNuGetProbeDirs(cspDir);

        // Register managed assembly resolver
        ResolveEventHandler assemblyResolver = (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            foreach (var dir in probePaths)
            {
                var path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path))
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += assemblyResolver;

        // Pre-load native libraries so that libraries using NativeLibrary.TryLoad
        // internally (e.g. Microsoft.Data.SqlClient for SNI) can find them.
        PreloadNativeLibraries(nativeProbePaths);

        try
        {
            // Set working directory to CSP directory for relative path resolution
            Directory.SetCurrentDirectory(cspDir);

            foreach (var propertySet in project.PropertySets)
            {
                Log($"Processing property set: {propertySet.Name}");
                RunPropertySet(propertySet, cspDir, project.Variables, probePaths);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            AppDomain.CurrentDomain.AssemblyResolve -= assemblyResolver;
        }
    }

    private void RunPropertySet(CspPropertySet propertySet, string cspDir, Dictionary<string, string> variables, List<string> probePaths)
    {
        // Resolve template path relative to CSP directory
        var templatePath = Path.GetFullPath(Path.Combine(cspDir, propertySet.TemplatePath));
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Template not found: {templatePath}");

        Log($"  Template: {templatePath}");

        // 1. Resolve template dependency graph
        var registry = new TemplateRegistry();
        var rootClassName = registry.Resolve(templatePath);

        Log($"  Resolved {registry.Entries.Count} template(s)");

        // 2. Collect assembly names from all templates' <%@ Assembly %> directives
        var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in registry.Entries.Values)
        {
            foreach (var asm in entry.Parsed.Assemblies)
            {
                if (!string.IsNullOrEmpty(asm.Name))
                    assemblyNames.Add(asm.Name);
            }
        }

        // 3. Generate C# source for each template
        var generator = new TemplateCodeGenerator();
        var sources = new Dictionary<string, string>();

        // Build map of registered template names to parsed templates for MergeProperties
        var registeredTemplates = registry.Entries.ToDictionary(e => e.Key, e => e.Value.Parsed);

        foreach (var (className, entry) in registry.Entries)
        {
            var source = generator.GenerateClass(className, entry.Parsed, registeredTemplates);
            sources[className] = source;

            if (_verbose)
                Log($"  Generated class: {className}");
        }

        // 4. Collect Assembly Src files
        foreach (var entry in registry.Entries.Values)
        {
            foreach (var asm in entry.Parsed.Assemblies)
            {
                if (!string.IsNullOrEmpty(asm.Src))
                {
                    var srcPath = Path.GetFullPath(
                        Path.Combine(Path.GetDirectoryName(entry.AbsolutePath)!, asm.Src));
                    if (File.Exists(srcPath))
                    {
                        var srcContent = File.ReadAllText(srcPath);
                        srcContent = TemplateCompiler.PrepareInlineSource(srcContent);
                        var srcName = Path.GetFileNameWithoutExtension(srcPath);
                        sources.TryAdd(srcName, srcContent);
                        Log($"  Included source: {srcPath}");
                    }
                }
            }
        }

        // 5. Compile all templates into one assembly (with optional cache)
        var compiler = new TemplateCompiler(assemblyNames, probePaths);
        var typeMap = compiler.Compile(sources, _cache);

        var cacheStatus = compiler.CacheHit switch
        {
            true => " (cached)",
            false => " (compiled)",
            _ => "",
        };
        Log($"  Compiled {typeMap.Count} template type(s){cacheStatus}");

        // 6. Instantiate root template
        if (!typeMap.TryGetValue(rootClassName, out var rootType))
            throw new InvalidOperationException($"Root template class '{rootClassName}' not found in compiled assembly");

        var template = (CodeTemplateBase)Activator.CreateInstance(rootType)!;
        template.CodeTemplateInfo.DirectoryName = Path.GetDirectoryName(templatePath);

        // 7. Set properties from CSP
        PropertyDeserializer.SetProperties(template, propertySet.Properties, variables);

        Log($"  Executing template...");

        // 8. Execute template (RenderToString triggers Generate() via template body)
        template.RenderToString();

        Log($"  Done: {propertySet.Name}");
    }

    /// <summary>
    /// Pre-loads native DLLs from the given directories so that libraries
    /// using NativeLibrary.TryLoad internally (e.g. Microsoft.Data.SqlClient for SNI)
    /// can find their dependencies.
    /// </summary>
    private static void PreloadNativeLibraries(List<string> nativeProbePaths)
    {
        foreach (var dir in nativeProbePaths)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var dll in Directory.GetFiles(dir, "*.dll"))
                NativeLibrary.TryLoad(dll, out _);
        }
    }

    /// <summary>
    /// Resolves assembly and native library probe directories from the consumer project's
    /// NuGet dependency graph by parsing obj/project.assets.json.
    /// Returns (managedLibDirs, nativeLibDirs).
    /// </summary>
    private static (List<string> managedDirs, List<string> nativeDirs) ResolveNuGetProbeDirs(string projectDir)
    {
        var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
            return ([], []);

        using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = doc.RootElement;

        var packageFolders = new List<string>();
        if (root.TryGetProperty("packageFolders", out var folders))
        {
            foreach (var folder in folders.EnumerateObject())
                packageFolders.Add(folder.Name);
        }

        if (packageFolders.Count == 0)
            return ([], []);

        // Build platform-specific RID list for native library resolution
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var rids = OperatingSystem.IsWindows()
            ? new[] { RuntimeInformation.RuntimeIdentifier, $"win-{arch}", "win" }
            : OperatingSystem.IsLinux()
                ? new[] { RuntimeInformation.RuntimeIdentifier, $"linux-{arch}", "linux" }
                : new[] { RuntimeInformation.RuntimeIdentifier, $"osx-{arch}", "osx" };

        // Platform-specific managed dirs go first (runtimes/<rid>/lib/<tfm>/)
        // so they take priority over generic facade assemblies in lib/<tfm>/
        var runtimeManagedDirs = new List<string>();
        var genericManagedDirs = new List<string>();
        var nativeDirs = new List<string>();

        if (root.TryGetProperty("libraries", out var libraries))
        {
            foreach (var lib in libraries.EnumerateObject())
            {
                if (!lib.Value.TryGetProperty("path", out var pathEl))
                    continue;
                var libPath = pathEl.GetString();
                if (libPath == null)
                    continue;

                foreach (var folder in packageFolders)
                {
                    var pkgRoot = Path.Combine(folder, libPath);

                    // Generic managed: lib/<tfm>/
                    var libDir = Path.Combine(pkgRoot, "lib");
                    if (Directory.Exists(libDir))
                    {
                        foreach (var tfmDir in Directory.GetDirectories(libDir))
                            genericManagedDirs.Add(tfmDir);
                    }

                    // Platform-specific: runtimes/<rid>/lib/<tfm>/ and runtimes/<rid>/native/
                    foreach (var rid in rids)
                    {
                        var runtimeLibDir = Path.Combine(pkgRoot, "runtimes", rid, "lib");
                        if (Directory.Exists(runtimeLibDir))
                        {
                            foreach (var tfmDir in Directory.GetDirectories(runtimeLibDir))
                                runtimeManagedDirs.Add(tfmDir);
                        }

                        var nativeDir = Path.Combine(pkgRoot, "runtimes", rid, "native");
                        if (Directory.Exists(nativeDir))
                            nativeDirs.Add(nativeDir);
                    }
                }
            }
        }

        // Platform-specific dirs first, then generic — so runtime-specific implementations
        // are preferred over reference/facade assemblies that throw PlatformNotSupportedException
        var managedDirs = runtimeManagedDirs.Concat(genericManagedDirs).ToList();
        return (managedDirs, nativeDirs);
    }

    private void Log(string message)
    {
        if (_verbose)
            Console.WriteLine(message);
        else
            Console.WriteLine(message);
    }
}
