using System.Text;
using OpenSmith.Engine;

namespace OpenSmith.Compilation;

/// <summary>
/// Converts a ParsedTemplate (from CstParser) into compilable C# source code
/// for a class extending CodeTemplateBase.
/// </summary>
public class TemplateCodeGenerator
{
    private static readonly HashSet<string> DefaultBlockedNamespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "CodeSmith.Engine",
        "CodeSmith.CustomProperties",
    };

    private static readonly HashSet<string> DefaultBaseImports =
    [
        "System",
        "System.IO",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text",
        "System.Text.RegularExpressions",
        "System.Diagnostics",
        "System.ComponentModel",
        "System.Xml.Serialization",
        "OpenSmith.Engine",
    ];

    private static readonly Dictionary<string, string> DefaultTypeRewrites = new()
    {
        ["CodeSmith.CustomProperties.StringCollection"] = "List<string>",
        ["ICSharpCode.NRefactory.SupportedLanguage"] = "OpenSmith.Engine.SupportedLanguage",
        ["ICSharpCode.NRefactory.Ast.AttributeSection"] = "OpenSmith.Engine.AttributeSection",
    };

    private static readonly Dictionary<string, string> DefaultSourceRewrites = new()
    {
        ["ICSharpCode.NRefactory.SupportedLanguage"] = "OpenSmith.Engine.SupportedLanguage",
        ["ICSharpCode.NRefactory.Ast.AttributeSection"] = "OpenSmith.Engine.AttributeSection",
        ["using CodeSmith.Engine;"] = "// using CodeSmith.Engine; // stripped",
        ["typeof(CodeTemplate)"] = "typeof(CodeTemplateBase)",
    };

    private readonly HashSet<string> _blockedNamespaces;
    private readonly HashSet<string> _baseImports;
    private readonly Dictionary<string, string> _typeRewrites;
    private readonly Dictionary<string, string> _sourceRewrites;

    public TemplateCodeGenerator(
        HashSet<string>? blockedNamespaces = null,
        IEnumerable<string>? additionalImports = null,
        Dictionary<string, string>? typeRewrites = null,
        Dictionary<string, string>? sourceRewrites = null)
    {
        _blockedNamespaces = blockedNamespaces ?? DefaultBlockedNamespaces;
        _baseImports = new HashSet<string>(DefaultBaseImports);
        if (additionalImports != null)
            foreach (var ns in additionalImports)
                _baseImports.Add(ns);
        _typeRewrites = typeRewrites ?? DefaultTypeRewrites;
        _sourceRewrites = sourceRewrites ?? DefaultSourceRewrites;
    }

    /// <summary>
    /// Generates a C# class for the template, optionally merging properties from registered sub-templates.
    /// </summary>
    public string GenerateClass(string className, ParsedTemplate template,
        Dictionary<string, ParsedTemplate>? registeredTemplates = null)
    {
        var sb = new StringBuilder();

        // Usings
        var imports = new HashSet<string>(_baseImports);
        foreach (var imp in template.Imports)
        {
            if (!_blockedNamespaces.Contains(imp))
                imports.Add(imp);
        }
        foreach (var ns in imports.OrderBy(n => n))
            sb.AppendLine($"using {ns};");

        sb.AppendLine();
        sb.AppendLine("namespace OpenSmith.CompiledTemplates;");
        sb.AppendLine();
        sb.AppendLine($"public class {className} : CodeTemplateBase");
        sb.AppendLine("{");

        // Properties from directives
        var emittedProperties = new HashSet<string>();
        foreach (var prop in template.Properties)
        {
            var typeName = ResolveType(prop.TypeName ?? "string");
            sb.AppendLine($"    public {typeName} {prop.Name} {{ get; set; }}");
            emittedProperties.Add(prop.Name);
        }

        // Merge properties from registered sub-templates (MergeProperties="True")
        if (registeredTemplates != null)
        {
            // Detect member names already defined in script blocks to avoid duplicates.
            // Uses regex to find property/field/method declarations with access modifiers.
            var scriptDefinedMembers = new HashSet<string>();
            var joinedScripts = string.Join("\n", template.ScriptBlocks);
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(joinedScripts,
                    @"(?:private|public|protected|internal)\s+\S+(?:\.\S+)*\s+(\w+)\s*[{(=;]"))
            {
                scriptDefinedMembers.Add(m.Groups[1].Value);
            }

            foreach (var reg in template.Registers)
            {
                if (!reg.MergeProperties) continue;
                if (!registeredTemplates.TryGetValue(reg.Name, out var subTemplate)) continue;

                var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(reg.ExcludeProperties))
                {
                    foreach (var ex in reg.ExcludeProperties.Split(',', StringSplitOptions.TrimEntries))
                        excluded.Add(ex);
                }

                foreach (var subProp in subTemplate.Properties)
                {
                    if (emittedProperties.Contains(subProp.Name)) continue;
                    if (excluded.Contains(subProp.Name)) continue;
                    if (scriptDefinedMembers.Contains(subProp.Name)) continue;

                    var typeName = ResolveType(subProp.TypeName ?? "string");
                    sb.AppendLine($"    public {typeName} {subProp.Name} {{ get; set; }}");
                    emittedProperties.Add(subProp.Name);
                }
            }
        }

        // Map properties
        foreach (var map in template.Maps)
        {
            var mapProp = GenerateMapProperty(map);
            if (mapProp != null)
                sb.AppendLine($"    {mapProp}");
        }

        sb.AppendLine();

        // Script blocks (pasted as-is, with source rewrites)
        foreach (var script in template.ScriptBlocks)
        {
            var rewritten = ApplySourceRewrites(script);
            sb.AppendLine(rewritten);
        }

        // RenderToString override
        if (template.Nodes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    public override string RenderToString()");
            sb.AppendLine("    {");
            sb.AppendLine("        var __sb = new StringBuilder();");
            sb.AppendLine("        Response.SetOutput(__sb);");

            foreach (var node in template.Nodes)
            {
                switch (node)
                {
                    case TextNode text:
                        sb.AppendLine($"        __sb.Append({EscapeString(text.Text)});");
                        break;
                    case ExpressionNode expr:
                        sb.AppendLine($"        __sb.Append({expr.Expression.Trim()});");
                        break;
                    case CodeBlockNode code:
                        sb.AppendLine($"        {ApplySourceRewrites(code.Code)}");
                        break;
                }
            }

            sb.AppendLine("        return __sb.ToString().TrimEnd('\\r', '\\n');");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string SanitizeClassName(string templatePath)
    {
        var name = Path.GetFileName(templatePath);
        return name.Replace('.', '_').Replace('-', '_').Replace(' ', '_');
    }

    private string ResolveType(string typeName)
    {
        if (_typeRewrites.TryGetValue(typeName, out var rewritten))
            return rewritten;
        return typeName;
    }

    private static string? GenerateMapProperty(MapDirective map)
    {
        if (map.Name.Contains("CSharpAlias", StringComparison.OrdinalIgnoreCase))
            return $"public CSharpAliasMap {map.Name} => CSharpAliasMap.Instance;";
        if (map.Name.Contains("CSharpKeyWordEscape", StringComparison.OrdinalIgnoreCase))
            return $"public CSharpKeyWordEscapeMap {map.Name} => CSharpKeyWordEscapeMap.Instance;";
        return null;
    }

    private string ApplySourceRewrites(string source)
    {
        foreach (var (from, to) in _sourceRewrites)
            source = source.Replace(from, to);
        return source;
    }

    private static string EscapeString(string text)
    {
        // Use verbatim string to handle multiline text with all special chars
        var escaped = text.Replace("\"", "\"\"");
        return $"@\"{escaped}\"";
    }
}
