using BenchmarkDotNet.Attributes;
using OpenSmith.Benchmarks.Helpers;
using OpenSmith.Compilation;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks recursive template dependency resolution (file I/O + CstParser.Parse).
/// </summary>
[MemoryDiagnoser]
public class TemplateRegistryBenchmarks
{
    [Benchmark(Baseline = true)]
    public string Resolve_Entities()
    {
        var registry = new TemplateRegistry();
        return registry.Resolve(BenchmarkTestContext.GetTemplatePath("Entities.cst"));
    }

    [Benchmark]
    public string Resolve_Dbml()
    {
        var registry = new TemplateRegistry();
        return registry.Resolve(BenchmarkTestContext.GetTemplatePath("Dbml.cst"));
    }

    [Benchmark]
    public string Resolve_SingleTemplate()
    {
        var registry = new TemplateRegistry();
        return registry.Resolve(BenchmarkTestContext.GetTemplatePath("Internal", "Enums.cst"));
    }
}
