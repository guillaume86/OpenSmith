using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using OpenSmith.Benchmarks.Helpers;
using OpenSmith.Cli;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks Roslyn compilation of generated template C# sources.
/// This is the most likely bottleneck in the pipeline.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 5)]
public class CompilationBenchmarks
{
    private Dictionary<string, string> _entitiesSources = null!;
    private Dictionary<string, string> _dbmlSources = null!;
    private Dictionary<string, string> _singleTemplateSources = null!;

    [GlobalSetup]
    public void Setup()
    {
        _entitiesSources = PrepareSources("Entities.cst");
        _dbmlSources = PrepareSources("Dbml.cst");
        _singleTemplateSources = PrepareSources(Path.Combine("Internal", "Enums.cst"));
    }

    [Benchmark(Baseline = true)]
    public Dictionary<string, Type> Compile_Entities() =>
        new TemplateCompiler().Compile(_entitiesSources);

    [Benchmark]
    public Dictionary<string, Type> Compile_Dbml() =>
        new TemplateCompiler().Compile(_dbmlSources);

    [Benchmark]
    public Dictionary<string, Type> Compile_SingleTemplate() =>
        new TemplateCompiler().Compile(_singleTemplateSources);

    private static Dictionary<string, string> PrepareSources(string templateRelativePath)
    {
        var templatePath = BenchmarkTestContext.GetTemplatePath(templateRelativePath);
        var registry = new TemplateRegistry();
        var rootClassName = registry.Resolve(templatePath);

        var generator = new TemplateCodeGenerator();
        var registeredTemplates = registry.Entries.ToDictionary(e => e.Key, e => e.Value.Parsed);
        var sources = new Dictionary<string, string>();

        foreach (var (className, entry) in registry.Entries)
            sources[className] = generator.GenerateClass(className, entry.Parsed, registeredTemplates);

        // Include Assembly Src files
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

        return sources;
    }
}
