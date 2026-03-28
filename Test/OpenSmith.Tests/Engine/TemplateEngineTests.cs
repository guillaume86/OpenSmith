using OpenSmith.Engine;

namespace OpenSmith.Tests.Engine;

public class TemplateEngineTests
{
    public class CstParserTests
    {
        [Fact]
        public void ParsesCodeTemplateDirective()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" Description="Test" %>
                Hello World
                """;
            var parsed = CstParser.Parse(cst);
            Assert.Equal("C#", parsed.Language);
            Assert.Single(parsed.Nodes);
            Assert.IsType<TextNode>(parsed.Nodes[0]);
        }

        [Fact]
        public void ParsesPropertyDirectives()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
                <%@ Property Name="MyProp" Type="System.String" Default="hello" Optional="True" %>
                <%= MyProp %>
                """;
            var parsed = CstParser.Parse(cst);
            Assert.Single(parsed.Properties);
            Assert.Equal("MyProp", parsed.Properties[0].Name);
            Assert.Equal("System.String", parsed.Properties[0].TypeName);
            Assert.Equal("hello", parsed.Properties[0].Default);
        }

        [Fact]
        public void ParsesMapDirective()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
                <%@ Map Name="CSharpAlias" Src="System-CSharpAlias.csmap" Reverse="False" %>
                """;
            var parsed = CstParser.Parse(cst);
            Assert.Single(parsed.Maps);
            Assert.Equal("CSharpAlias", parsed.Maps[0].Name);
            Assert.Equal("System-CSharpAlias.csmap", parsed.Maps[0].Src);
        }

        [Fact]
        public void ParsesExpressionBlocks()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
                Hello <%= Name %>!
                """;
            var parsed = CstParser.Parse(cst);
            Assert.Equal(3, parsed.Nodes.Count);
            Assert.IsType<TextNode>(parsed.Nodes[0]);
            Assert.IsType<ExpressionNode>(parsed.Nodes[1]);
            Assert.IsType<TextNode>(parsed.Nodes[2]);
            Assert.Equal(" Name ", ((ExpressionNode)parsed.Nodes[1]).Expression);
        }

        [Fact]
        public void ParsesCodeBlocks()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
                <% if (true) { %>
                Hello
                <% } %>
                """;
            var parsed = CstParser.Parse(cst);
            Assert.Contains(parsed.Nodes, n => n is CodeBlockNode);
        }

        [Fact]
        public void StandaloneCodeLine_DoesNotProduceLeadingNewline()
        {
            // A standalone code-only line should not inject a newline into the following text
            var cst = "<%@ CodeTemplate Language=\"C#\" TargetLanguage=\"Text\" %>\r\n<% if (true) { %>\r\nHello\r\n<% } %>\r\n";
            var parsed = CstParser.Parse(cst);

            // The text node containing "Hello" should NOT start with \r\n
            var helloNode = parsed.Nodes.OfType<TextNode>().Single();
            Assert.True(helloNode.Text.StartsWith("Hello"),
                $"Expected TextNode to start with 'Hello' but got: '{helloNode.Text.Replace("\r", "\\r").Replace("\n", "\\n")}'");
        }

        [Fact]
        public void StandaloneCodeLines_NoBlankLineBetweenCodeBlocks()
        {
            // Two consecutive standalone code lines should not produce any whitespace TextNode between them
            var cst = "<%@ CodeTemplate Language=\"C#\" TargetLanguage=\"Text\" %>\r\nBefore\r\n<% code1(); %>\r\n<% code2(); %>\r\nAfter\r\n";
            var parsed = CstParser.Parse(cst);

            var textNodes = parsed.Nodes.OfType<TextNode>().ToList();
            // Should only have "Before\r\n" and "After\r\n" — no whitespace-only TextNodes
            foreach (var tn in textNodes)
            {
                Assert.False(string.IsNullOrWhiteSpace(tn.Text),
                    $"Found whitespace-only TextNode: '{tn.Text.Replace("\r", "\\r").Replace("\n", "\\n")}'");
            }
        }

        [Fact]
        public void LeadingDirectiveWhitespace_IsStripped()
        {
            // Directives at the top should not leave leading blank lines
            var cst = "<%@ CodeTemplate Language=\"C#\" TargetLanguage=\"Text\" %>\r\n<%@ Property Name=\"X\" Type=\"System.String\" Optional=\"True\" %>\r\n<%@ Import Namespace=\"System\" %>\r\nHello World\r\n";
            var parsed = CstParser.Parse(cst);

            Assert.Single(parsed.Nodes);
            var text = Assert.IsType<TextNode>(parsed.Nodes[0]);
            Assert.StartsWith("Hello", text.Text.TrimStart());
            // Should NOT have leading newlines
            Assert.False(text.Text.StartsWith("\r") || text.Text.StartsWith("\n"),
                $"TextNode has leading newlines: '{text.Text.Replace("\r", "\\r").Replace("\n", "\\n")}'");
        }

        [Fact]
        public void MixedContentLine_PreservesWhitespace()
        {
            // A line with text + expression should preserve leading whitespace
            // Use a code block first so the expression line is NOT the first node
            var cst = "<%@ CodeTemplate Language=\"C#\" TargetLanguage=\"Text\" %>\r\nBefore\r\n    <%= Name %> World\r\n";
            var parsed = CstParser.Parse(cst);

            // Should have: TextNode("Before\r\n    "), ExpressionNode, TextNode(" World\r\n")
            var textNodes = parsed.Nodes.OfType<TextNode>().ToList();
            Assert.Contains(textNodes, t => t.Text.Contains("    "));
        }

        [Fact]
        public void ExpressionLine_NotTreatedAsStandalone()
        {
            // A line with only an expression should preserve surrounding whitespace
            // Use a preceding text line so expression line isn't first
            var cst = "<%@ CodeTemplate Language=\"C#\" TargetLanguage=\"Text\" %>\r\nBefore\r\n  <%= Name %>\r\n";
            var parsed = CstParser.Parse(cst);

            // Should have TextNode with "  " before the expression
            var textNodes = parsed.Nodes.OfType<TextNode>().ToList();
            Assert.Contains(textNodes, t => t.Text.Contains("  "));
        }

        [Fact]
        public void ParsesScriptBlock()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
                <script runat="template">
                public string GetName() { return "test"; }
                </script>
                """;
            var parsed = CstParser.Parse(cst);
            Assert.NotEmpty(parsed.ScriptBlocks);
            Assert.Contains("GetName", parsed.ScriptBlocks[0]);
        }

        [Fact]
        public void ParsesRegisterDirective()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
                <%@ Register Name="SubTemplate" Template="Internal\Sub.cst" MergeProperties="True" ExcludeProperties="Database" %>
                """;
            var parsed = CstParser.Parse(cst);
            Assert.Single(parsed.Registers);
            Assert.Equal("SubTemplate", parsed.Registers[0].Name);
            Assert.Equal(@"Internal\Sub.cst", parsed.Registers[0].Template);
        }

        [Fact]
        public void ParsesAssemblyDirective()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
                <%@ Assembly Name="Dbml" Path="..\Common" %>
                <%@ Assembly Name="System.Design" %>
                """;
            var parsed = CstParser.Parse(cst);
            Assert.Equal(2, parsed.Assemblies.Count);
            Assert.Equal("Dbml", parsed.Assemblies[0].Name);
            Assert.Equal(@"..\Common", parsed.Assemblies[0].Path);
            Assert.Equal("System.Design", parsed.Assemblies[1].Name);
            Assert.Null(parsed.Assemblies[1].Path);
        }

        [Fact]
        public void ParsesImportDirective()
        {
            var cst = """
                <%@ CodeTemplate Language="C#" TargetLanguage="Text" %>
                <%@ Import Namespace="System.IO" %>
                <%@ Import Namespace="System.Text" %>
                """;
            var parsed = CstParser.Parse(cst);
            Assert.Equal(2, parsed.Imports.Count);
            Assert.Equal("System.IO", parsed.Imports[0]);
            Assert.Equal("System.Text", parsed.Imports[1]);
        }
    }

    public class MapFileTests
    {
        [Fact]
        public void CSharpAliasMap_MapsSystemTypesToAliases()
        {
            var map = CSharpAliasMap.Instance;
            Assert.Equal("int", map["System.Int32"]);
            Assert.Equal("string", map["System.String"]);
            Assert.Equal("bool", map["System.Boolean"]);
            Assert.Equal("decimal", map["System.Decimal"]);
            Assert.Equal("double", map["System.Double"]);
            Assert.Equal("float", map["System.Single"]);
            Assert.Equal("long", map["System.Int64"]);
            Assert.Equal("short", map["System.Int16"]);
            Assert.Equal("byte", map["System.Byte"]);
            Assert.Equal("char", map["System.Char"]);
            Assert.Equal("object", map["System.Object"]);
            Assert.Equal("byte[]", map["System.Byte[]"]);
        }

        [Fact]
        public void CSharpAliasMap_PassesThroughUnknownTypes()
        {
            var map = CSharpAliasMap.Instance;
            Assert.Equal("MyCustomType", map["MyCustomType"]);
        }

        [Fact]
        public void CSharpKeyWordEscapeMap_EscapesCSharpKeywords()
        {
            var map = CSharpKeyWordEscapeMap.Instance;
            Assert.Equal("@class", map["class"]);
            Assert.Equal("@string", map["string"]);
            Assert.Equal("@int", map["int"]);
            Assert.Equal("@event", map["event"]);
            Assert.Equal("@object", map["object"]);
            Assert.Equal("@namespace", map["namespace"]);
        }

        [Fact]
        public void CSharpKeyWordEscapeMap_PassesThroughNonKeywords()
        {
            var map = CSharpKeyWordEscapeMap.Instance;
            Assert.Equal("myVariable", map["myVariable"]);
            Assert.Equal("AskId", map["AskId"]);
        }
    }

    public class CodeTemplateBaseTests
    {
        private class TestTemplate : CodeTemplateBase
        {
            public string Name { get; set; }
            public int Count { get; set; }
        }

        [Fact]
        public void CopyPropertiesTo_CopiesMatchingProperties()
        {
            var source = new TestTemplate { Name = "hello", Count = 42 };
            var target = new TestTemplate();

            source.CopyPropertiesTo(target);

            Assert.Equal("hello", target.Name);
            Assert.Equal(42, target.Count);
        }

        [Fact]
        public void Progress_TracksSteps()
        {
            var template = new TestTemplate();
            template.Progress.MaximumValue = 10;
            template.Progress.Step = 1;

            template.Progress.PerformStep();
            template.Progress.PerformStep();

            Assert.Equal(2, template.Progress.Value);
        }

        [Fact]
        public void Response_CapturesWriteLine()
        {
            var template = new TestTemplate();
            template.Response.WriteLine("hello");
            template.Response.WriteLine("world");

            Assert.Equal(2, template.Response.Lines.Count);
            Assert.Equal("hello", template.Response.Lines[0]);
        }

        [Fact]
        public void Response_WriteFeedsIntoRenderOutput()
        {
            // When Response is connected to a StringBuilder (as in RenderToString),
            // Write/WriteLine should append to that output
            var sb = new System.Text.StringBuilder();
            var template = new TestTemplate();
            template.Response.SetOutput(sb);

            template.Response.Write("hello");
            template.Response.WriteLine("world");

            Assert.Equal("helloworld\r\n", sb.ToString());
        }

        [Fact]
        public void Response_WriteLineFormatsCorrectly()
        {
            var sb = new System.Text.StringBuilder();
            var template = new TestTemplate();
            template.Response.SetOutput(sb);

            template.Response.WriteLine("{0} = {1}", "key", "value");

            Assert.Equal("key = value\r\n", sb.ToString());
        }

        [Fact]
        public void CodeTemplateInfo_DirectoryName_IsSet()
        {
            var template = new TestTemplate();
            template.CodeTemplateInfo.DirectoryName = @"C:\Templates";

            Assert.Equal(@"C:\Templates", template.CodeTemplateInfo.DirectoryName);
        }
    }
}
