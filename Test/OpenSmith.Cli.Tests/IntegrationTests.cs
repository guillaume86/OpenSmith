using OpenSmith.Cli;
using OpenSmith.Engine;

namespace OpenSmith.Cli.Tests;

public class IntegrationTests
{
    [Fact]
    public void CompilesEnumsCst()
    {
        var templatePath = Path.Combine(TestContext.RepoRoot, "CSharp", "Internal", "Enums.cst");
        if (!File.Exists(templatePath))
            return;

        var content = File.ReadAllText(templatePath);
        var parsed = CstParser.Parse(content);
        var generator = new TemplateCodeGenerator();
        var className = TemplateCodeGenerator.SanitizeClassName(templatePath);
        var source = generator.GenerateClass(className, parsed);

        var compiler = new TemplateCompiler();
        var typeMap = compiler.Compile(new Dictionary<string, string>
        {
            [className] = source,
        });

        Assert.True(typeMap.ContainsKey(className), $"Expected type '{className}' in compiled assembly");
    }

    [Fact]
    public void CompilesDbmlCstWithDependencies()
    {
        var templatePath = Path.Combine(TestContext.RepoRoot, "CSharp", "Dbml.cst");
        if (!File.Exists(templatePath))
            return;

        // Resolve template graph
        var registry = new TemplateRegistry();
        var rootClassName = registry.Resolve(templatePath);

        // Generate C# source for all templates
        var generator = new TemplateCodeGenerator();
        var sources = new Dictionary<string, string>();
        foreach (var (className, entry) in registry.Entries)
        {
            sources[className] = generator.GenerateClass(className, entry.Parsed);
        }

        // Compile
        var compiler = new TemplateCompiler();
        var typeMap = compiler.Compile(sources);

        Assert.True(typeMap.ContainsKey(rootClassName),
            $"Expected root type '{rootClassName}' in compiled assembly");
    }

    [Fact]
    public void CompilesEntitiesCstWithAllSubTemplates()
    {
        var templatePath = Path.Combine(TestContext.RepoRoot, "CSharp", "Entities.cst");
        if (!File.Exists(templatePath))
            return;

        // Resolve template graph
        var registry = new TemplateRegistry();
        var rootClassName = registry.Resolve(templatePath);

        // Generate C# source for all templates
        var generator = new TemplateCodeGenerator();
        var sources = new Dictionary<string, string>();
        var registeredTemplates = registry.Entries.ToDictionary(e => e.Key, e => e.Value.Parsed);
        foreach (var (className, entry) in registry.Entries)
        {
            sources[className] = generator.GenerateClass(className, entry.Parsed, registeredTemplates);
        }

        // Add Assembly Src files
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
                    }
                }
            }
        }

        // Compile
        var compiler = new TemplateCompiler();
        var typeMap = compiler.Compile(sources);

        Assert.True(typeMap.ContainsKey(rootClassName),
            $"Expected root type '{rootClassName}' in compiled assembly. Types: {string.Join(", ", typeMap.Keys)}");

        // Should have multiple template types
        Assert.True(typeMap.Count >= 8,
            $"Expected at least 8 template types, got {typeMap.Count}: {string.Join(", ", typeMap.Keys)}");
    }

    [Theory]
    [InlineData("Managers.cst")]
    [InlineData("Queries.cst")]
    public void CompilesAdditionalTemplatesWithSubTemplates(string templateName)
    {
        var templatePath = Path.Combine(TestContext.RepoRoot, "CSharp", templateName);
        if (!File.Exists(templatePath))
            return;

        var registry = new TemplateRegistry();
        var rootClassName = registry.Resolve(templatePath);

        var generator = new TemplateCodeGenerator();
        var sources = new Dictionary<string, string>();
        var registeredTemplates = registry.Entries.ToDictionary(e => e.Key, e => e.Value.Parsed);
        foreach (var (className, entry) in registry.Entries)
        {
            sources[className] = generator.GenerateClass(className, entry.Parsed, registeredTemplates);
        }

        // Add Assembly Src files
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
                    }
                }
            }
        }

        var compiler = new TemplateCompiler();
        var typeMap = compiler.Compile(sources);

        Assert.True(typeMap.ContainsKey(rootClassName),
            $"Expected root type '{rootClassName}' in compiled assembly");
    }
}
