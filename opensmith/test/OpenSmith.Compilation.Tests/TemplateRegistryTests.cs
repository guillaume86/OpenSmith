using OpenSmith.Compilation;

namespace OpenSmith.Cli.Tests;

public class TemplateRegistryTests
{
    private readonly string _fixtureDir;

    public TemplateRegistryTests()
    {
        _fixtureDir = Path.Combine(Path.GetTempPath(), "OpenSmithRegistryTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public void ResolvesRootTemplateOnly()
    {
        var templatePath = Path.Combine(_fixtureDir, "Root.cst");
        File.WriteAllText(templatePath, """
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Property Name="Name" Type="System.String" %>
            Hello
            """);

        var registry = new TemplateRegistry();
        var rootClass = registry.Resolve(templatePath);

        Assert.Single(registry.Entries);
        Assert.Equal("Root_cst", rootClass);
        Assert.True(registry.Entries.ContainsKey("Root_cst"));
    }

    [Fact]
    public void ResolvesRegisteredSubTemplates()
    {
        var internalDir = Path.Combine(_fixtureDir, "Internal");
        Directory.CreateDirectory(internalDir);

        File.WriteAllText(Path.Combine(_fixtureDir, "Parent.cst"), """
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Register Name="ChildTemplate" Template="Internal\Child.cst" MergeProperties="True" %>
            """);

        File.WriteAllText(Path.Combine(internalDir, "Child.cst"), """
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Property Name="Value" Type="System.String" %>
            """);

        var registry = new TemplateRegistry();
        registry.Resolve(Path.Combine(_fixtureDir, "Parent.cst"));

        Assert.Equal(2, registry.Entries.Count);
        Assert.True(registry.Entries.ContainsKey("Parent_cst"));
        Assert.True(registry.Entries.ContainsKey("ChildTemplate"));
    }

    [Fact]
    public void ResolvesNestedRegistrations()
    {
        var internalDir = Path.Combine(_fixtureDir, "Internal");
        Directory.CreateDirectory(internalDir);

        File.WriteAllText(Path.Combine(_fixtureDir, "Root.cst"), """
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Register Name="Middle" Template="Internal\Middle.cst" %>
            """);

        File.WriteAllText(Path.Combine(internalDir, "Middle.cst"), """
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Register Name="Leaf" Template="Leaf.cst" %>
            """);

        File.WriteAllText(Path.Combine(internalDir, "Leaf.cst"), """
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            """);

        var registry = new TemplateRegistry();
        registry.Resolve(Path.Combine(_fixtureDir, "Root.cst"));

        Assert.Equal(3, registry.Entries.Count);
        Assert.True(registry.Entries.ContainsKey("Root_cst"));
        Assert.True(registry.Entries.ContainsKey("Middle"));
        Assert.True(registry.Entries.ContainsKey("Leaf"));
    }

    [Fact]
    public void SkipsMissingSubTemplateGracefully()
    {
        File.WriteAllText(Path.Combine(_fixtureDir, "Root.cst"), """
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Register Name="Missing" Template="DoesNotExist.cst" %>
            """);

        var registry = new TemplateRegistry();
        registry.Resolve(Path.Combine(_fixtureDir, "Root.cst"));

        Assert.Single(registry.Entries);
    }

}
