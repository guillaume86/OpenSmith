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

    public TemplateAssemblyLoadContext(string publishDir)
        : base("OpenSmith.Templates", isCollectible: false)
    {
        _publishDir = publishDir;

        // Find a .deps.json to initialize the resolver
        var depsFiles = Directory.GetFiles(publishDir, "*.deps.json");
        if (depsFiles.Length > 0)
        {
            var mainDll = Path.ChangeExtension(depsFiles[0], ".dll");
            if (File.Exists(mainDll))
                _resolver = new AssemblyDependencyResolver(mainDll);
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try the resolver first (reads .deps.json for correct resolution)
        if (_resolver != null)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
                return LoadFromAssemblyPath(path);
        }

        // Fallback: direct file probe in publish directory
        var name = assemblyName.Name;
        if (name != null)
        {
            var dllPath = Path.Combine(_publishDir, name + ".dll");
            if (File.Exists(dllPath))
                return LoadFromAssemblyPath(dllPath);
        }

        // Return null to fall back to the default ALC
        // (this is how host assemblies like OpenSmith.dll are found)
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
