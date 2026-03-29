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

        // Pre-scan all templates to collect NuGet directives for dependency resolution
        var allNuGetDirectives = new List<NuGetDirective>();
        string? templateDir = null;

        foreach (var propertySet in project.PropertySets)
        {
            var templatePath = Path.GetFullPath(Path.Combine(cspDir, propertySet.TemplatePath));
            templateDir ??= Path.GetDirectoryName(templatePath);

            var registry = new TemplateRegistry();
            registry.Resolve(templatePath);

            foreach (var entry in registry.Entries.Values)
                allNuGetDirectives.AddRange(entry.Parsed.NuGetPackages);
        }

        // Resolve dependencies via dotnet publish (or cache hit)
        var publisher = new DependencyPublisher(templateDir ?? cspDir, allNuGetDirectives, _verbose);
        publisher.Publish();

        // Create custom ALC for runtime assembly + native library resolution
        var alc = new TemplateAssemblyLoadContext(publisher.PublishDirectory!);
        TemplateAssemblyLoadContext.Current = alc;
        PropertyDeserializer.SetLoadContext(alc);

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            // Set working directory to CSP directory for relative path resolution
            Directory.SetCurrentDirectory(cspDir);

            foreach (var propertySet in project.PropertySets)
            {
                Log($"Processing property set: {propertySet.Name}");
                RunPropertySet(propertySet, cspDir, project.Variables, publisher.PublishDirectory!, publisher.Fingerprint);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    private void RunPropertySet(CspPropertySet propertySet, string cspDir, Dictionary<string, string> variables,
        string publishDirectory, string? publishFingerprint)
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

        // 2. Generate C# source for each template
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

        // 3. Collect Assembly Src files
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

        // 4. Compile all templates into one assembly (with optional cache)
        var compiler = new TemplateCompiler(publishDirectory);
        var typeMap = compiler.Compile(sources, _cache, publishFingerprint);

        var cacheStatus = compiler.CacheHit switch
        {
            true => " (cached)",
            false => " (compiled)",
            _ => "",
        };
        Log($"  Compiled {typeMap.Count} template type(s){cacheStatus}");

        // 5. Instantiate root template
        if (!typeMap.TryGetValue(rootClassName, out var rootType))
            throw new InvalidOperationException($"Root template class '{rootClassName}' not found in compiled assembly");

        var template = (CodeTemplateBase)Activator.CreateInstance(rootType)!;
        template.CodeTemplateInfo.DirectoryName = Path.GetDirectoryName(templatePath);

        // 6. Set properties from CSP
        PropertyDeserializer.SetProperties(template, propertySet.Properties, variables);

        Log($"  Executing template...");

        // 7. Execute template (RenderToString triggers Generate() via template body)
        template.RenderToString();

        // 8. Log registered outputs and references
        LogPostExecution(template, _verbose, Log);

        Log($"  Done: {propertySet.Name}");
    }

    public static void LogPostExecution(CodeTemplateBase template, bool verbose, Action<string> log)
    {
        foreach (var reference in template.References)
            log($"  WARNING: RegisterReference is not supported — ignored '{reference}'");

        if (verbose)
        {
            foreach (var output in template.Outputs)
            {
                var parent = string.IsNullOrEmpty(output.ParentFileName)
                    ? ""
                    : $" (parent: {output.ParentFileName})";
                log($"  Registered output: {output.FileName}{parent}");
            }
        }
    }

    private void Log(string message)
    {
        Console.WriteLine(message);
    }
}
