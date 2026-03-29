using System.Reflection;
using System.Runtime.Loader;

namespace OpenSmith.Compilation;

/// <summary>
/// Custom AssemblyLoadContext that uses AssemblyDependencyResolver to load
/// managed and native assemblies from a dotnet publish output directory.
/// Falls back to the default ALC for assemblies already loaded by the host
/// (e.g., OpenSmith.Engine's CodeTemplateBase).
/// </summary>
public class TemplateAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver? _resolver;
    private readonly string _publishDir;
    private readonly HashSet<string> _hostAssemblyNames;

    /// <summary>
    /// The current instance, set during CspRunner execution so that TemplateCompiler
    /// can load compiled template assemblies into this context.
    /// </summary>
    public static TemplateAssemblyLoadContext? Current { get; set; }

    public TemplateAssemblyLoadContext(string publishDir)
        : base("OpenSmith.Templates", isCollectible: false)
    {
        _publishDir = publishDir;

        // Snapshot assemblies already loaded in the default ALC so we never shadow them.
        // This is critical: if we load OpenSmith.dll from the publish dir, CodeTemplateBase
        // becomes a different Type and IsAssignableFrom checks fail.
        _hostAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in Default.Assemblies)
        {
            var name = asm.GetName().Name;
            if (name != null)
                _hostAssemblyNames.Add(name);
        }

        // Find a .deps.json to initialize the resolver
        if (!string.IsNullOrEmpty(publishDir) && Directory.Exists(publishDir))
        {
            var depsFiles = Directory.GetFiles(publishDir, "*.deps.json");
            if (depsFiles.Length > 0)
            {
                var mainDll = Path.ChangeExtension(depsFiles[0], ".dll");
                if (File.Exists(mainDll))
                    _resolver = new AssemblyDependencyResolver(mainDll);
            }
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Never shadow assemblies already loaded by the host (e.g., OpenSmith.dll).
        // Returning null falls back to the default ALC.
        if (assemblyName.Name != null && _hostAssemblyNames.Contains(assemblyName.Name))
            return null;

        // Try the resolver first (reads .deps.json for correct resolution)
        if (_resolver != null)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
                return LoadFromAssemblyPath(path);
        }

        // Fallback: direct file probe in publish directory
        if (assemblyName.Name != null)
        {
            var dllPath = Path.Combine(_publishDir, assemblyName.Name + ".dll");
            if (File.Exists(dllPath))
                return LoadFromAssemblyPath(dllPath);
        }

        // Return null to fall back to the default ALC
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (_resolver != null)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null)
                return LoadUnmanagedDllFromPath(path);
        }

        return IntPtr.Zero;
    }
}
