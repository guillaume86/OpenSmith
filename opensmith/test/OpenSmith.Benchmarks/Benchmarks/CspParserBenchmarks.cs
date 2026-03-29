using BenchmarkDotNet.Attributes;
using OpenSmith.Benchmarks.Helpers;
using OpenSmith.Compilation;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks CSP XML project file parsing.
/// </summary>
[MemoryDiagnoser]
public class CspParserBenchmarks
{
    private string _cspXml = null!;
    private string _cspXmlLarge = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cspXml = File.ReadAllText(
            Path.Combine(BenchmarkTestContext.GetTemplatePath("Sample-Generator.csp")));

        // Synthesize a large CSP with many property sets for stress testing
        _cspXmlLarge = GenerateLargeCsp(50);
    }

    [Benchmark(Baseline = true)]
    public CspProject Parse_SampleGenerator() => CspParser.Parse(_cspXml);

    [Benchmark]
    public CspProject Parse_LargeCsp() => CspParser.Parse(_cspXmlLarge);

    private static string GenerateLargeCsp(int propertySetCount)
    {
        var propertySets = string.Concat(Enumerable.Range(0, propertySetCount).Select(i => $"""
            <propertySet name="Entities{i}" template="Templates\Entities.cst">
              <property name="DbmlFile">.\Generated\Db{i}.dbml</property>
              <property name="Framework">v45</property>
              <property name="IncludeDataServices">False</property>
              <property name="IncludeDataRules">False</property>
              <property name="AuditingEnabled">False</property>
              <property name="IncludeDataContract">True</property>
              <property name="IncludeXmlSerialization">False</property>
              <property name="IncludeManyToMany">False</property>
              <property name="AssociationNamingSuffix">ListSuffix</property>
              <property name="OutputDirectory">.\Generated\Entities{i}</property>
              <property name="BaseDirectory">.\Generated\</property>
              <property name="InterfaceDirectory" />
            </propertySet>
        """));

        return $"""
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <variables />
              <propertySets>
            {propertySets}
              </propertySets>
            </codeSmith>
            """;
    }
}
