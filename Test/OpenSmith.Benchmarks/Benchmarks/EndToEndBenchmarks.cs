using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using OpenSmith.Benchmarks.Helpers;
using OpenSmith.Cli;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// End-to-end benchmark of the full template pipeline: resolve -> generate -> compile.
/// Replicates CspRunner.RunPropertySet steps 1-4 without file output or DB access.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 3)]
public class EndToEndBenchmarks
{
    [Benchmark(Baseline = true)]
    public Dictionary<string, Type> FullPipeline_Entities() =>
        RunPipeline("Entities.cst");

    [Benchmark]
    public Dictionary<string, Type> FullPipeline_Dbml() =>
        RunPipeline("Dbml.cst");

    [Benchmark]
    public Dictionary<string, Type> FullPipeline_MassiveIncludeList_Entities() =>
        RunCspPipeline(
            Path.Combine(BenchmarkTestContext.RepoRoot, "DiffTest-MassiveIncludeList", "SampleDb-Generator.csp"),
            "Entities");

    [Benchmark]
    public Dictionary<string, Type> FullPipeline_MassiveIncludeList_Dbml() =>
        RunCspPipeline(
            Path.Combine(BenchmarkTestContext.RepoRoot, "DiffTest-MassiveIncludeList", "SampleDb-Generator.csp"),
            "Dbml");

    private static Dictionary<string, Type> RunPipeline(string templateRelativePath)
    {
        var templatePath = BenchmarkTestContext.GetTemplatePath(templateRelativePath);
        return RunPipelineForTemplate(templatePath);
    }

    private static Dictionary<string, Type> RunCspPipeline(string cspPath, string propertySetName)
    {
        // Step 0: Parse CSP and resolve template path
        var cspXml = File.ReadAllText(cspPath);
        var project = CspParser.Parse(cspXml);
        var cspDir = Path.GetDirectoryName(cspPath)!;
        var propertySet = project.PropertySets.First(ps =>
            ps.Name.Equals(propertySetName, StringComparison.OrdinalIgnoreCase));
        var templatePath = Path.GetFullPath(Path.Combine(cspDir, propertySet.TemplatePath));

        return RunPipelineForTemplate(templatePath);
    }

    private static Dictionary<string, Type> RunPipelineForTemplate(string templatePath)
    {
        // Step 1: Resolve template dependency graph
        var registry = new TemplateRegistry();
        var rootClassName = registry.Resolve(templatePath);

        // Step 2: Generate C# source for each template
        var generator = new TemplateCodeGenerator();
        var registeredTemplates = registry.Entries.ToDictionary(e => e.Key, e => e.Value.Parsed);
        var sources = new Dictionary<string, string>();

        foreach (var (className, entry) in registry.Entries)
            sources[className] = generator.GenerateClass(className, entry.Parsed, registeredTemplates);

        // Step 3: Collect Assembly Src files
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
                        var srcContent = TemplateCompiler.PrepareInlineSource(File.ReadAllText(srcPath));
                        sources.TryAdd(Path.GetFileNameWithoutExtension(srcPath), srcContent);
                    }
                }
            }
        }

        // Step 4: Compile all templates
        return new TemplateCompiler().Compile(sources);
    }
}
