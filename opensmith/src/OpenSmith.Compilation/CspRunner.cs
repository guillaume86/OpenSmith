using OpenSmith.Engine;

namespace OpenSmith.Compilation;

/// <summary>
/// Orchestrates CSP project execution: parse, compile templates, set properties, generate.
/// </summary>
public class CspRunner
{
    private readonly bool _verbose;
    private readonly TemplateCompilationCache _cache;

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

        try
        {
            // Set working directory to CSP directory for relative path resolution
            Directory.SetCurrentDirectory(cspDir);

            foreach (var propertySet in project.PropertySets)
            {
                Log($"Processing property set: {propertySet.Name}");
                RunPropertySet(propertySet, cspDir, project.Variables);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    private void RunPropertySet(CspPropertySet propertySet, string cspDir, Dictionary<string, string> variables)
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
        var compiler = new TemplateCompiler(assemblyNames);
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

    private void Log(string message)
    {
        if (_verbose)
            Console.WriteLine(message);
        else
            Console.WriteLine(message);
    }
}
