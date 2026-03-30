using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenSmith.Engine;

/// <summary>
/// Parses CodeSmith .cst template files into a structured representation.
/// </summary>
public static class CstParser
{
    private static readonly Regex DirectiveRegex = new(
        @"<%@\s+(\w+)\s+(.*?)%>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AttributeRegex = new(
        @"(\w+)\s*=\s*""([^""]*?)""",
        RegexOptions.Compiled);

    private static readonly Regex ScriptRegex = new(
        @"<script\s+runat\s*=\s*""template"">(.*?)</script>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static ParsedTemplate Parse(string content)
    {
        var result = new ParsedTemplate();

        // Extract and remove script blocks first
        content = ScriptRegex.Replace(content, m =>
        {
            result.ScriptBlocks.Add(m.Groups[1].Value.Trim());
            return "";
        });

        // Extract and remove directives
        content = DirectiveRegex.Replace(content, m =>
        {
            ParseDirective(result, m.Groups[1].Value, m.Groups[2].Value);
            return "";
        });

        // Strip standalone code lines and leading blank lines before parsing body
        content = StripStandaloneCodeLines(content);
        content = content.TrimStart();

        // Parse remaining content into text/code/expression nodes
        ParseBody(result, content);

        // Post-process: strip newlines injected by code blocks (including multi-line ones)
        StripCodeBlockNewlines(result.Nodes);

        return result;
    }

    /// <summary>
    /// After parsing, strip the leading newline from TextNodes that follow multi-line CodeBlockNodes.
    /// Single-line code blocks are already handled by StripStandaloneCodeLines.
    /// Multi-line code blocks (where the code spans multiple lines) leave a trailing newline
    /// after the closing %&gt; that should be consumed.
    /// </summary>
    private static void StripCodeBlockNewlines(List<TemplateNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not CodeBlockNode code)
                continue;

            // Only strip after multi-line code blocks (code contains newlines)
            if (!code.Code.Contains('\n'))
                continue;

            // Strip leading \r\n from following TextNode
            if (i + 1 < nodes.Count && nodes[i + 1] is TextNode nextText)
            {
                var t = nextText.Text;
                if (t.StartsWith("\r\n"))
                    nodes[i + 1] = new TextNode(t[2..]);
                else if (t.StartsWith("\n"))
                    nodes[i + 1] = new TextNode(t[1..]);
            }
        }

        // Remove empty TextNodes
        nodes.RemoveAll(n => n is TextNode t && t.Text.Length == 0);
    }

    // Regex to detect lines that contain ONLY code blocks <% ... %> (not expression blocks <%= ... %>) and whitespace
    private static readonly Regex StandaloneCodeLineRegex = new(
        @"^[ \t]*(?:<%(?!=).*?%>[ \t]*)+$",
        RegexOptions.Compiled);

    /// <summary>
    /// Removes trailing newlines from lines that contain only code blocks and whitespace.
    /// This prevents standalone code lines (like &lt;% if (x) { %&gt;) from injecting blank lines.
    /// </summary>
    internal static string StripStandaloneCodeLines(string content)
    {
        var lines = content.Split('\n');
        var sb = new StringBuilder(content.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (StandaloneCodeLineRegex.IsMatch(line))
            {
                // Emit only the code block markers without surrounding whitespace/newline
                sb.Append(line.Trim());
            }
            else
            {
                sb.Append(lines[i]);
                if (i < lines.Length - 1)
                    sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    private static void ParseDirective(ParsedTemplate result, string type, string attributes)
    {
        var attrs = ParseAttributes(attributes);

        switch (type)
        {
            case "CodeTemplate":
                attrs.TryGetValue("Language", out var lang);
                result.Language = lang;
                break;

            case "Property":
                result.Properties.Add(new PropertyDirective
                {
                    Name = attrs.GetValueOrDefault("Name"),
                    TypeName = attrs.GetValueOrDefault("Type"),
                    Default = attrs.GetValueOrDefault("Default"),
                    Optional = string.Equals(attrs.GetValueOrDefault("Optional"), "True", StringComparison.OrdinalIgnoreCase),
                    Category = attrs.GetValueOrDefault("Category"),
                    Description = attrs.GetValueOrDefault("Description"),
                });
                break;

            case "Map":
                result.Maps.Add(new MapDirective
                {
                    Name = attrs.GetValueOrDefault("Name"),
                    Src = attrs.GetValueOrDefault("Src"),
                    Reverse = string.Equals(attrs.GetValueOrDefault("Reverse"), "True", StringComparison.OrdinalIgnoreCase),
                    Description = attrs.GetValueOrDefault("Description"),
                });
                break;

            case "Register":
                result.Registers.Add(new RegisterDirective
                {
                    Name = attrs.GetValueOrDefault("Name"),
                    Template = attrs.GetValueOrDefault("Template"),
                    MergeProperties = string.Equals(attrs.GetValueOrDefault("MergeProperties"), "True", StringComparison.OrdinalIgnoreCase),
                    ExcludeProperties = attrs.GetValueOrDefault("ExcludeProperties"),
                });
                break;

            case "Assembly":
                result.Assemblies.Add(new AssemblyDirective
                {
                    Name = attrs.GetValueOrDefault("Name"),
                    Path = attrs.GetValueOrDefault("Path"),
                    Src = attrs.GetValueOrDefault("Src"),
                });
                break;

            case "NuGet":
                result.NuGetPackages.Add(new NuGetDirective
                {
                    Package = attrs.GetValueOrDefault("Package"),
                    Version = attrs.GetValueOrDefault("Version"),
                });
                break;

            case "Import":
                var ns = attrs.GetValueOrDefault("Namespace");
                if (ns != null)
                    result.Imports.Add(ns);
                break;
        }
    }

    private static Dictionary<string, string> ParseAttributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttributeRegex.Matches(text))
        {
            result[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return result;
    }

    private static void ParseBody(ParsedTemplate result, string content)
    {
        int pos = 0;
        while (pos < content.Length)
        {
            int tagStart = content.IndexOf("<%", pos);
            if (tagStart < 0)
            {
                // Rest is text
                var remaining = content[pos..];
                if (!string.IsNullOrWhiteSpace(remaining))
                    result.Nodes.Add(new TextNode(remaining));
                break;
            }

            // Text before the tag
            if (tagStart > pos)
            {
                var text = content[pos..tagStart];
                if (!string.IsNullOrWhiteSpace(text) || result.Nodes.Count > 0)
                    result.Nodes.Add(new TextNode(text));
            }

            // Determine tag type
            int tagEnd = content.IndexOf("%>", tagStart + 2);
            if (tagEnd < 0)
            {
                result.Nodes.Add(new TextNode(content[pos..]));
                break;
            }

            string tagContent = content[(tagStart + 2)..tagEnd];

            var trimmed = tagContent.TrimStart();
            if (trimmed.StartsWith("="))
            {
                result.Nodes.Add(new ExpressionNode(trimmed[1..]));
            }
            else
            {
                result.Nodes.Add(new CodeBlockNode(tagContent.Trim()));
            }

            pos = tagEnd + 2;
        }
    }
}

public class ParsedTemplate
{
    public string Language { get; set; }
    public List<PropertyDirective> Properties { get; } = new();
    public List<MapDirective> Maps { get; } = new();
    public List<RegisterDirective> Registers { get; } = new();
    public List<AssemblyDirective> Assemblies { get; } = new();
    public List<NuGetDirective> NuGetPackages { get; } = new();
    public List<string> Imports { get; } = new();
    public List<string> ScriptBlocks { get; } = new();
    public List<TemplateNode> Nodes { get; } = new();
}

public abstract class TemplateNode { }

public class TextNode : TemplateNode
{
    public string Text { get; }
    public TextNode(string text) => Text = text;
}

public class ExpressionNode : TemplateNode
{
    public string Expression { get; }
    public ExpressionNode(string expression) => Expression = expression;
}

public class CodeBlockNode : TemplateNode
{
    public string Code { get; }
    public CodeBlockNode(string code) => Code = code;
}

public class PropertyDirective
{
    public string Name { get; set; }
    public string TypeName { get; set; }
    public string Default { get; set; }
    public bool Optional { get; set; }
    public string Category { get; set; }
    public string Description { get; set; }
}

public class MapDirective
{
    public string Name { get; set; }
    public string Src { get; set; }
    public bool Reverse { get; set; }
    public string Description { get; set; }
}

public class RegisterDirective
{
    public string Name { get; set; }
    public string Template { get; set; }
    public bool MergeProperties { get; set; }
    public string ExcludeProperties { get; set; }
}

public class AssemblyDirective
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string Src { get; set; }
}

public class NuGetDirective
{
    public string Package { get; set; }
    public string Version { get; set; }
}
