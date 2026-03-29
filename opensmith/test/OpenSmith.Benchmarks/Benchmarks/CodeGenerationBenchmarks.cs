using BenchmarkDotNet.Attributes;
using OpenSmith.Benchmarks.Helpers;
using OpenSmith.Compilation;
using OpenSmith.Engine;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks C# source code generation from pre-resolved parsed templates.
/// </summary>
[MemoryDiagnoser]
public class CodeGenerationBenchmarks
{
    private string _rootClassName = null!;
    private Dictionary<string, ParsedTemplate> _registeredTemplates = null!;
    private Dictionary<string, TemplateEntry> _entries = null!;

    [GlobalSetup]
    public void Setup()
    {
        var registry = new TemplateRegistry();
        _rootClassName = registry.Resolve(BenchmarkTestContext.GetTemplatePath("Entities.cst"));
        _registeredTemplates = registry.Entries.ToDictionary(e => e.Key, e => e.Value.Parsed);
        _entries = registry.Entries;
    }

    [Benchmark]
    public Dictionary<string, string> GenerateAll_Entities()
    {
        var generator = new TemplateCodeGenerator();
        var sources = new Dictionary<string, string>();
        foreach (var (className, entry) in _entries)
            sources[className] = generator.GenerateClass(className, entry.Parsed, _registeredTemplates);
        return sources;
    }
}
