using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using OpenSmith.Benchmarks.Helpers;
using OpenSmith.Compilation;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Compares cold Roslyn compilation vs cache hit for the Entities template tree.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 5)]
public class CachedCompilationBenchmarks
{
    private Dictionary<string, string> _sources = null!;
    private TemplateCompilationCache _cache = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sources = PrepareSources("Entities.cst");
        _cache = new TemplateCompilationCache();

        // Prime the cache with a cold compile
        new TemplateCompiler().Compile(_sources, _cache);
    }

    [Benchmark(Baseline = true)]
    public Dictionary<string, Type> Compile_NoCache() =>
        new TemplateCompiler().Compile(_sources);

    [Benchmark]
    public Dictionary<string, Type> Compile_CacheHit() =>
        new TemplateCompiler().Compile(_sources, _cache);

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
