using OpenSmith.Compilation;
using OpenSmith.Engine;

namespace OpenSmith.Cli.Tests;

public class TemplateCodeGeneratorTests
{
    private readonly TemplateCodeGenerator _generator = new();

    [Fact]
    public void GeneratesClassWithCorrectName()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            """);

        var source = _generator.GenerateClass("MyTemplate_cst", template);

        Assert.Contains("public class MyTemplate_cst : CodeTemplateBase", source);
    }

    [Fact]
    public void GeneratesStringProperty()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Property Name="OutputDirectory" Type="System.String" Default="" %>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("public System.String OutputDirectory { get; set; }", source);
    }

    [Fact]
    public void RewritesStringCollectionType()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Property Name="IgnoreList" Type="CodeSmith.CustomProperties.StringCollection" %>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("public List<string> IgnoreList { get; set; }", source);
        Assert.DoesNotContain("CodeSmith.CustomProperties", source);
    }

    [Fact]
    public void GeneratesMapPropertyForCSharpAlias()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Map Name="CSharpAlias" Src="System-CSharpAlias" %>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("public CSharpAliasMap CSharpAlias => CSharpAliasMap.Instance;", source);
    }

    [Fact]
    public void GeneratesMapPropertyForKeyWordEscape()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Map Name="CSharpKeyWordEscape" Src="CSharpKeyWordEscape.csmap" %>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("CSharpKeyWordEscapeMap CSharpKeyWordEscape => CSharpKeyWordEscapeMap.Instance;", source);
    }

    [Fact]
    public void IncludesScriptBlocksInClassBody()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <script runat="template">
            public void Generate()
            {
                Response.WriteLine("Hello");
            }
            </script>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("public void Generate()", source);
        Assert.Contains("Response.WriteLine(\"Hello\")", source);
    }

    [Fact]
    public void GeneratesRenderToStringWithTextNodes()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            Hello World
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("public override string RenderToString()", source);
        Assert.Contains("__sb.Append(", source);
        Assert.Contains("Hello World", source);
    }

    [Fact]
    public void GeneratesRenderToStringWithExpressionNodes()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Property Name="Name" Type="System.String" %>
            Hello <%= Name %>!
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("__sb.Append(Name);", source);
    }

    [Fact]
    public void GeneratesRenderToStringWithCodeBlocks()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <% if (true) { %>
            Hello
            <% } %>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("if (true) {", source);
    }

    [Fact]
    public void StripsBlockedNamespaces()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <%@ Import Namespace="CodeSmith.Engine" %>
            <%@ Import Namespace="CodeSmith.CustomProperties" %>
            <%@ Import Namespace="System.IO" %>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.DoesNotContain("using CodeSmith.Engine;", source);
        Assert.DoesNotContain("using CodeSmith.CustomProperties;", source);
        Assert.Contains("using System.IO;", source);
    }

    [Fact]
    public void RewritesICSharpCodeReferencesInScriptBlocks()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            <script runat="template">
            public void Test()
            {
                var lang = ICSharpCode.NRefactory.SupportedLanguage.CSharp;
            }
            </script>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("OpenSmith.Engine.SupportedLanguage.CSharp", source);
        Assert.DoesNotContain("ICSharpCode.NRefactory.SupportedLanguage", source);
    }

    [Fact]
    public void SanitizeClassName_ReplacesDotsAndDashes()
    {
        Assert.Equal("Dbml_cst", TemplateCodeGenerator.SanitizeClassName("Dbml.cst"));
        Assert.Equal("Entity_Generated_cst", TemplateCodeGenerator.SanitizeClassName("Entity.Generated.cst"));
        Assert.Equal("My_Template_cst", TemplateCodeGenerator.SanitizeClassName("My-Template.cst"));
    }

    [Fact]
    public void GeneratesNamespaceDeclaration()
    {
        var template = CstParser.Parse("""
            <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
            """);

        var source = _generator.GenerateClass("Test_cst", template);

        Assert.Contains("namespace OpenSmith.CompiledTemplates;", source);
    }
}
