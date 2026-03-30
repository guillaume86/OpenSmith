using System.Diagnostics;
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
        var totalSw = Stopwatch.StartNew();

        var absoluteCspPath = Path.GetFullPath(cspPath);
        var cspDir = Path.GetDirectoryName(absoluteCspPath)!;
        var xml = File.ReadAllText(absoluteCspPath);
        var project = CspParser.Parse(xml);

        LogVerbose($"Parsed CSP: {project.PropertySets.Count} property set(s)");

        // Pre-scan all templates to collect NuGet directives for dependency resolution
        var scanSw = Stopwatch.StartNew();
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
        scanSw.Stop();
        LogVerbose($"  Dependencies: resolving NuGet directives... [{FormatElapsed(scanSw)}]");

        // Resolve dependencies via dotnet publish (or cache hit)
        var depSw = Stopwatch.StartNew();
        var publisher = new DependencyPublisher(templateDir ?? cspDir, allNuGetDirectives, _verbose);
        publisher.Publish();
        depSw.Stop();
        LogVerbose($"Dependencies resolved [{FormatElapsed(depSw)}]");

        // Create custom ALC for runtime assembly + native library resolution
        var alc = new TemplateAssemblyLoadContext(publisher.PublishDirectory!);
        TemplateAssemblyLoadContext.Current = alc;
        PropertyDeserializer.SetLoadContext(alc);

        var succeeded = 0;
        var failed = 0;
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            // Set working directory to CSP directory for relative path resolution
            Directory.SetCurrentDirectory(cspDir);

            foreach (var propertySet in project.PropertySets)
            {
                LogVerbose($"Processing property set: {propertySet.Name}");
                try
                {
                    RunPropertySet(propertySet, cspDir, project.Variables, publisher.PublishDirectory!, publisher.Fingerprint);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (_verbose)
                        LogVerbose($"  FAILED: {propertySet.Name} — {ex.Message}");
                    else
                        throw;
                }
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }

        totalSw.Stop();
        var elapsedMs = (long)totalSw.Elapsed.TotalMilliseconds;
        Log($"\nDone rendering outputs: {succeeded} succeeded, {failed} failed ({elapsedMs}ms).");
    }

    private void RunPropertySet(CspPropertySet propertySet, string cspDir, Dictionary<string, string> variables,
        string publishDirectory, string? publishFingerprint)
    {
        // Resolve template path relative to CSP directory
        var templatePath = Path.GetFullPath(Path.Combine(cspDir, propertySet.TemplatePath));
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Template not found: {templatePath}");

        LogVerbose($"  Template: {templatePath}");

        // 1. Resolve template dependency graph
        var sw = Stopwatch.StartNew();
        var registry = new TemplateRegistry();
        var rootClassName = registry.Resolve(templatePath);
        sw.Stop();

        LogVerbose($"  Resolved {registry.Entries.Count} template(s) [{FormatElapsed(sw)}]");

        // 2. Generate C# source for each template
        sw.Restart();
        var generator = new TemplateCodeGenerator();
        var sources = new Dictionary<string, string>();

        // Build map of registered template names to parsed templates for MergeProperties
        var registeredTemplates = registry.Entries.ToDictionary(e => e.Key, e => e.Value.Parsed);

        foreach (var (className, entry) in registry.Entries)
        {
            var source = generator.GenerateClass(className, entry.Parsed, registeredTemplates);
            sources[className] = source;

            if (_verbose)
                LogVerbose($"  Generated class: {className}");
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
                        LogVerbose($"  Included source: {srcPath}");
                    }
                }
            }
        }
        sw.Stop();

        LogVerbose($"  Generated {sources.Count} source(s) [{FormatElapsed(sw)}]");

        // 4. Compile all templates into one assembly (with optional cache)
        sw.Restart();
        var compiler = new TemplateCompiler(publishDirectory);
        var typeMap = compiler.Compile(sources, _cache, publishFingerprint);
        sw.Stop();

        var cacheStatus = compiler.CacheHit switch
        {
            true => " (cached)",
            false => " (compiled)",
            _ => "",
        };
        LogVerbose($"  Compiled {typeMap.Count} template type(s){cacheStatus} [{FormatElapsed(sw)}]");

        // 5. Instantiate root template
        if (!typeMap.TryGetValue(rootClassName, out var rootType))
            throw new InvalidOperationException($"Root template class '{rootClassName}' not found in compiled assembly");

        var template = (CodeTemplateBase)Activator.CreateInstance(rootType)!;
        template.CodeTemplateInfo.DirectoryName = Path.GetDirectoryName(templatePath);

        // 5b. Propagate schema provider hints from template <%@ Property %> directives to CSP properties.
        // Directive attributes (e.g. DeepLoad="true" on <%@ Property %>) provide static hints.
        // Also check for matching CSP boolean properties (e.g. IncludeViews=True in the .csp)
        // and OR them in, so the schema provider loads views/functions when the template requests them.
        var rootParsed = registry.Entries[rootClassName].Parsed;
        foreach (var propDirective in rootParsed.Properties)
        {
            if (!propertySet.Properties.TryGetValue(propDirective.Name, out var cspProp))
                continue;

            bool includeViews = propDirective.IncludeViews
                || (propertySet.Properties.TryGetValue("IncludeViews", out var ivProp)
                    && string.Equals(ivProp.Value, "True", StringComparison.OrdinalIgnoreCase));
            bool includeFunctions = propDirective.IncludeFunctions
                || (propertySet.Properties.TryGetValue("IncludeFunctions", out var ifProp)
                    && string.Equals(ifProp.Value, "True", StringComparison.OrdinalIgnoreCase));

            if (!propDirective.DeepLoad && !includeViews && !includeFunctions)
                continue;

            cspProp.ProviderHints = new Dictionary<string, bool>
            {
                ["DeepLoad"] = propDirective.DeepLoad,
                ["IncludeViews"] = includeViews,
                ["IncludeFunctions"] = includeFunctions,
            };
        }

        // 6. Set properties from CSP
        sw.Restart();
        PropertyDeserializer.SetProperties(template, propertySet.Properties, variables);
        sw.Stop();
        LogVerbose($"  Properties set [{FormatElapsed(sw)}]");

        LogVerbose($"  Executing template...");

        // 7. Execute template (RenderToString triggers Generate() via template body)
        CodeTemplateBase.ResetCounters();
        CodeTemplateBase.EnableDeferredWrites();
        sw.Restart();
        template.RenderToString();
        CodeTemplateBase.FlushDeferredWrites();
        sw.Stop();

        var filesWritten = CodeTemplateBase.FilesWrittenCount;

        // 8. Log registered outputs and references
        LogPostExecution(template, _verbose, LogVerbose);

        LogVerbose($"  Done: {propertySet.Name} — {filesWritten} file(s) written, {template.Outputs.Count} output(s) registered [{FormatElapsed(sw)}]");
    }

    public static void LogPostExecution(CodeTemplateBase template, bool verbose, Action<string> log)
    {
        if (template.References.Count > 0)
        {
            log($"  WARNING: RegisterReference is not supported — ignored {template.References.Count} reference(s)");
            if (verbose)
            {
                foreach (var reference in template.References)
                    log($"    - {reference}");
            }
        }

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

    private void LogVerbose(string message)
    {
        if (_verbose)
            Console.WriteLine(message);
    }

    private static string FormatElapsed(Stopwatch sw)
    {
        var elapsed = sw.Elapsed;
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.TotalMinutes:0.00}m";
        return $"{elapsed.TotalSeconds:0.00}s";
    }
}
