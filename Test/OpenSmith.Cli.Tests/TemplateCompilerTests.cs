using OpenSmith.Cli;
using OpenSmith.Engine;

namespace OpenSmith.Cli.Tests;

public class TemplateCompilerTests
{
    [Fact]
    public void CompilesSimpleTemplateClass()
    {
        var source = """
            using System.Text;
            using OpenSmith.Engine;

            namespace OpenSmith.CompiledTemplates;

            public class SimpleTemplate : CodeTemplateBase
            {
                public string Name { get; set; }

                public override string RenderToString()
                {
                    var __sb = new StringBuilder();
                    __sb.Append("Hello ");
                    __sb.Append(Name);
                    return __sb.ToString();
                }
            }
            """;

        var compiler = new TemplateCompiler();
        var typeMap = compiler.Compile(new Dictionary<string, string>
        {
            ["SimpleTemplate"] = source,
        });

        Assert.Single(typeMap);
        Assert.True(typeMap.ContainsKey("SimpleTemplate"));

        var instance = (CodeTemplateBase)Activator.CreateInstance(typeMap["SimpleTemplate"])!;
        typeMap["SimpleTemplate"].GetProperty("Name")!.SetValue(instance, "World");
        Assert.Equal("Hello World", instance.RenderToString());
    }

    [Fact]
    public void CompilesMultipleClassesInOneAssembly()
    {
        var parentSource = """
            using System.Text;
            using OpenSmith.Engine;

            namespace OpenSmith.CompiledTemplates;

            public class ParentTemplate : CodeTemplateBase
            {
                public override string RenderToString()
                {
                    var child = Create<ChildTemplate>();
                    return "Parent+" + child.RenderToString();
                }
            }
            """;

        var childSource = """
            using System.Text;
            using OpenSmith.Engine;

            namespace OpenSmith.CompiledTemplates;

            public class ChildTemplate : CodeTemplateBase
            {
                public override string RenderToString()
                {
                    return "Child";
                }
            }
            """;

        var compiler = new TemplateCompiler();
        var typeMap = compiler.Compile(new Dictionary<string, string>
        {
            ["ParentTemplate"] = parentSource,
            ["ChildTemplate"] = childSource,
        });

        Assert.Equal(2, typeMap.Count);

        var parent = (CodeTemplateBase)Activator.CreateInstance(typeMap["ParentTemplate"])!;
        Assert.Equal("Parent+Child", parent.RenderToString());
    }

    [Fact]
    public void ThrowsOnCompilationError()
    {
        var badSource = """
            namespace OpenSmith.CompiledTemplates;
            public class Bad : NONEXISTENT_BASE { }
            """;

        var compiler = new TemplateCompiler();
        var ex = Assert.Throws<TemplateCompilationException>(() =>
            compiler.Compile(new Dictionary<string, string> { ["Bad"] = badSource }));

        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public void EndToEnd_CstParserToCompiler()
    {
        // Parse a simple CST template, generate C#, compile, and execute
        var cst = """
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Property Name="Greeting" Type="System.String" %>
            Hello <%= Greeting %>!
            """;

        var parsed = CstParser.Parse(cst);
        var generator = new TemplateCodeGenerator();
        var source = generator.GenerateClass("Greeter_cst", parsed);

        var compiler = new TemplateCompiler();
        var typeMap = compiler.Compile(new Dictionary<string, string>
        {
            ["Greeter_cst"] = source,
        });

        var template = (CodeTemplateBase)Activator.CreateInstance(typeMap["Greeter_cst"])!;
        typeMap["Greeter_cst"].GetProperty("Greeting")!.SetValue(template, "World");

        var result = template.RenderToString();
        Assert.Contains("Hello World!", result);
    }
}
