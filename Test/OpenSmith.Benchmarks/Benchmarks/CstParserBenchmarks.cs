using BenchmarkDotNet.Attributes;
using OpenSmith.Benchmarks.Helpers;
using OpenSmith.Engine;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks isolated CstParser.Parse on pre-loaded template content (no file I/O).
/// </summary>
[MemoryDiagnoser]
public class CstParserBenchmarks
{
    private string _entitiesCst = null!;
    private string _entityGeneratedCst = null!;
    private string _enumsCst = null!;

    [GlobalSetup]
    public void Setup()
    {
        _entitiesCst = File.ReadAllText(BenchmarkTestContext.GetTemplatePath("Entities.cst"));
        _entityGeneratedCst = File.ReadAllText(
            BenchmarkTestContext.GetTemplatePath("Internal", "Entity.Generated.cst"));
        _enumsCst = File.ReadAllText(
            BenchmarkTestContext.GetTemplatePath("Internal", "Enums.cst"));
    }

    [Benchmark(Baseline = true)]
    public ParsedTemplate Parse_Entities() => CstParser.Parse(_entitiesCst);

    [Benchmark]
    public ParsedTemplate Parse_EntityGenerated() => CstParser.Parse(_entityGeneratedCst);

    [Benchmark]
    public ParsedTemplate Parse_Enums() => CstParser.Parse(_enumsCst);
}
