using BenchmarkDotNet.Attributes;
using OpenSmith.Benchmarks.Helpers;
using OpenSmith.Cli;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks CSP XML project file parsing.
/// </summary>
[MemoryDiagnoser]
public class CspParserBenchmarks
{
    private string _cspXml = null!;
    private string _cspXmlMassive = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cspXml = File.ReadAllText(BenchmarkTestContext.GetCspPath("SampleDb-Generator.csp"));
        _cspXmlMassive = File.ReadAllText(
            Path.Combine(BenchmarkTestContext.RepoRoot, "DiffTest-MassiveIncludeList", "SampleDb-Generator.csp"));
    }

    [Benchmark(Baseline = true)]
    public CspProject Parse_SampleDbGenerator() => CspParser.Parse(_cspXml);

    [Benchmark]
    public CspProject Parse_MassiveIncludeList() => CspParser.Parse(_cspXmlMassive);
}
