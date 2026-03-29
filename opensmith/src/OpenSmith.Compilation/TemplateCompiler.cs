using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenSmith.Engine;

namespace OpenSmith.Compilation;

/// <summary>
/// Compiles generated C# source code into an in-memory assembly using Roslyn.
/// </summary>
public class TemplateCompiler
{
    private readonly List<MetadataReference> _references;

    /// <summary>
    /// Creates a compiler that resolves the given assembly names (from template directives)
    /// in addition to standard runtime and OpenSmith assemblies.
    /// </summary>
    public TemplateCompiler(IEnumerable<string>? assemblyNames = null)
    {
        _references = BuildMetadataReferences(assemblyNames ?? []);
    }

    /// <summary>
    /// Compiles the given C# source strings into an assembly and returns a map of class name to Type.
    /// When a cache is provided, attempts to load a previously compiled assembly before invoking Roslyn.
    /// </summary>
    public Dictionary<string, Type> Compile(Dictionary<string, string> sources, TemplateCompilationCache? cache = null)
    {
        // Try cache first
        string? hash = null;
        if (cache != null)
        {
            hash = cache.ComputeHash(sources);
            if (cache.TryLoadCached(hash, out var cachedBytes))
            {
                CacheHit = true;
                return LoadAssemblyTypes(cachedBytes);
            }
        }

        CacheHit = false;

        var syntaxTrees = new List<SyntaxTree>();
        foreach (var (name, source) in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: name + ".cs",
                options: new CSharpParseOptions(LanguageVersion.Latest));
            syntaxTrees.Add(tree);
        }

        var compilation = CSharpCompilation.Create(
            "OpenSmith.CompiledTemplates_" + Guid.NewGuid().ToString("N")[..8],
            syntaxTrees,
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Debug)
                .WithAllowUnsafe(false));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            throw new TemplateCompilationException(errors);
        }

        var assemblyBytes = ms.ToArray();

        // Store in cache
        if (cache != null && hash != null)
            cache.Store(hash, assemblyBytes);

        return LoadAssemblyTypes(assemblyBytes);
    }

    /// <summary>
    /// Indicates whether the last Compile call was a cache hit. Null if no cache was used.
    /// </summary>
    public bool? CacheHit { get; private set; }

    private static Dictionary<string, Type> LoadAssemblyTypes(byte[] assemblyBytes)
    {
        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(new MemoryStream(assemblyBytes));

        var typeMap = new Dictionary<string, Type>();
        foreach (var type in assembly.GetTypes())
        {
            if (typeof(CodeTemplateBase).IsAssignableFrom(type) && !type.IsAbstract)
                typeMap[type.Name] = type;
        }

        return typeMap;
    }

    /// <summary>
    /// Adds an additional source file (e.g., from Assembly Src directives) to be included in compilation.
    /// Returns the source content with namespace/using rewrites applied.
    /// </summary>
    public static string PrepareInlineSource(string sourceContent)
    {
        // Replace CodeSmith.Engine with OpenSmith.Engine
        sourceContent = sourceContent.Replace("using CodeSmith.Engine;", "using OpenSmith.Engine;");
        // Rewrite ICSharpCode references
        sourceContent = sourceContent.Replace("ICSharpCode.NRefactory.SupportedLanguage", "OpenSmith.Engine.SupportedLanguage");
        sourceContent = sourceContent.Replace("ICSharpCode.NRefactory.Ast.AttributeSection", "OpenSmith.Engine.AttributeSection");
        return sourceContent;
    }

    private static List<MetadataReference> BuildMetadataReferences(IEnumerable<string> assemblyNames)
    {
        var refs = new List<MetadataReference>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRef(string path)
        {
            if (File.Exists(path) && addedPaths.Add(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        // Add runtime assemblies
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in new[]
        {
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Console.dll",
            "System.IO.dll",
            "System.Text.RegularExpressions.dll",
            "System.ComponentModel.dll",
            "System.ComponentModel.Primitives.dll",
            "System.ComponentModel.TypeConverter.dll",
            "System.ComponentModel.DataAnnotations.dll",
            "System.ComponentModel.Annotations.dll",
            "System.Xml.ReaderWriter.dll",
            "System.Xml.XmlSerializer.dll",
            "System.Private.Xml.dll",
            "System.Private.Xml.Linq.dll",
            "System.Xml.Linq.dll",
            "System.ObjectModel.dll",
            "System.Diagnostics.Debug.dll",
            "System.Runtime.Extensions.dll",
            "netstandard.dll",
            "System.Runtime.Serialization.Primitives.dll",
        })
        {
            AddRef(Path.Combine(runtimeDir, dll));
        }

        // mscorlib / System.Private.CoreLib
        AddRef(typeof(object).Assembly.Location);

        // Always include OpenSmith engine
        var requestedNames = new HashSet<string>(assemblyNames, StringComparer.OrdinalIgnoreCase) { "OpenSmith" };

        // Resolve requested assemblies from loaded assemblies
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                continue;

            var asmName = asm.GetName().Name;
            if (asmName != null && requestedNames.Contains(asmName))
                AddRef(asm.Location);
        }

        // Also scan the application base directory for assemblies not yet loaded
        var appDir = AppContext.BaseDirectory;
        foreach (var name in requestedNames)
        {
            AddRef(Path.Combine(appDir, name + ".dll"));
        }

        return refs;
    }
}

public class TemplateCompilationException : Exception
{
    public List<string> Errors { get; }

    public TemplateCompilationException(List<string> errors)
        : base("Template compilation failed:\n" + string.Join("\n", errors))
    {
        Errors = errors;
    }
}
