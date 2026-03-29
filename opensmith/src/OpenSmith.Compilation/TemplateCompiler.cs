using System.Reflection;
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
    /// Creates a compiler with only runtime framework references (no additional dependencies).
    /// </summary>
    public TemplateCompiler() : this("") { }

    /// <summary>
    /// Creates a compiler that resolves MetadataReferences from the publish output directory.
    /// </summary>
    /// <param name="publishDirectory">Flat directory produced by DependencyPublisher containing all dependency DLLs.</param>
    public TemplateCompiler(string publishDirectory)
    {
        _references = BuildMetadataReferences(publishDirectory);
    }

    /// <summary>
    /// Compiles the given C# source strings into an assembly and returns a map of class name to Type.
    /// When a cache is provided, attempts to load a previously compiled assembly before invoking Roslyn.
    /// </summary>
    public Dictionary<string, Type> Compile(Dictionary<string, string> sources, TemplateCompilationCache? cache = null, string? publishFingerprint = null)
    {
        // Try cache first
        string? hash = null;
        if (cache != null)
        {
            hash = cache.ComputeHash(sources, publishFingerprint);
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
        var assembly = Assembly.Load(assemblyBytes);

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

    private static List<MetadataReference> BuildMetadataReferences(string publishDirectory)
    {
        var refs = new List<MetadataReference>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddManagedRef(string path)
        {
            if (!addedPaths.Add(path)) return;
            if (!IsManagedAssembly(path)) return;
            refs.Add(MetadataReference.CreateFromFile(path));
        }

        // Add all runtime framework assemblies
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
            TryAddManagedRef(dll);

        // Add host assemblies (e.g., OpenSmith.dll) so templates can reference CodeTemplateBase
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                TryAddManagedRef(asm.Location);
        }

        // Add all DLLs from the publish directory (template package + NuGet deps)
        if (!string.IsNullOrEmpty(publishDirectory) && Directory.Exists(publishDirectory))
        {
            foreach (var dll in Directory.GetFiles(publishDirectory, "*.dll"))
                TryAddManagedRef(dll);
        }

        return refs;
    }

    /// <summary>
    /// Checks whether a DLL file is a managed assembly by reading its PE header.
    /// Native DLLs (e.g., coreclr.dll, hostpolicy.dll) will return false.
    /// </summary>
    private static bool IsManagedAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException) { return false; }
        catch { return false; }
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
