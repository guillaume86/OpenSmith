using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenSmith.Engine;

/// <summary>
/// Merges generated code into existing files, preserving user-edited sections.
/// Used for .Editable.cst templates to insert/update named regions (e.g., Metadata class)
/// without overwriting user code.
/// </summary>
public class InsertClassMergeStrategy
{
    public enum NotFoundActionEnum
    {
        None,
        InsertInParent,
        InsertAtEnd,
    }

    public SupportedLanguage Language { get; }
    public string SectionName { get; }

    public bool OnlyInsertMatchingClass { get; set; }
    public bool PreserveClassAttributes { get; set; }
    public NotFoundActionEnum NotFoundAction { get; set; }
    public string NotFoundParent { get; set; }
    public bool MergeImports { get; set; }

    public InsertClassMergeStrategy(SupportedLanguage language, string sectionName)
    {
        Language = language;
        SectionName = sectionName;
    }

    /// <summary>
    /// Merges new content into existing content, preserving user code outside the named region.
    /// </summary>
    public string Merge(string existingContent, string newContent)
    {
        var result = new StringBuilder(existingContent);

        // Step 1: Merge imports if requested
        if (MergeImports)
            result = MergeUsingStatements(result, newContent);

        var existingText = result.ToString();

        // Step 2: Extract the region from new content
        var newRegion = ExtractRegion(newContent, SectionName);
        if (newRegion == null) return existingText;

        // Step 3: Find and replace the region in existing content, or insert it
        var existingRegionBounds = FindRegionBounds(existingText, SectionName);
        if (existingRegionBounds.HasValue)
        {
            // Replace existing region
            var (start, end) = existingRegionBounds.Value;
            return existingText[..start] + newRegion + existingText[end..];
        }

        // Region not found — insert based on NotFoundAction
        if (NotFoundAction == NotFoundActionEnum.InsertInParent && !string.IsNullOrEmpty(NotFoundParent))
        {
            return InsertInParentClass(existingText, NotFoundParent, newRegion);
        }

        return existingText;
    }

    private static StringBuilder MergeUsingStatements(StringBuilder existing, string newContent)
    {
        var existingText = existing.ToString();
        var existingUsings = ExtractUsings(existingText);
        var newUsings = ExtractUsings(newContent);

        var toAdd = newUsings.Where(u => !existingUsings.Contains(u)).ToList();
        if (toAdd.Count == 0) return existing;

        // Find the last using statement in existing and insert after it
        var lines = existingText.Split('\n');
        int lastUsingIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("using ") && lines[i].TrimEnd().EndsWith(";"))
                lastUsingIndex = i;
        }

        var result = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            result.Append(lines[i]);
            if (i < lines.Length - 1) result.Append('\n');

            if (i == lastUsingIndex)
            {
                foreach (var u in toAdd)
                {
                    result.Append('\n');
                    result.Append($"using {u};");
                }
            }
        }

        return result;
    }

    private static HashSet<string> ExtractUsings(string content)
    {
        var usings = new HashSet<string>();
        var regex = new Regex(@"^\s*using\s+([\w.]+)\s*;", RegexOptions.Multiline);
        foreach (Match m in regex.Matches(content))
        {
            usings.Add(m.Groups[1].Value);
        }
        return usings;
    }

    private static string ExtractRegion(string content, string regionName)
    {
        var pattern = $@"(\s*#region\s+{Regex.Escape(regionName)}.*?#endregion)";
        var match = Regex.Match(content, pattern, RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static (int start, int end)? FindRegionBounds(string content, string regionName)
    {
        var startPattern = $@"[ \t]*#region\s+{Regex.Escape(regionName)}";
        var startMatch = Regex.Match(content, startPattern);
        if (!startMatch.Success) return null;

        // Find from the beginning of the line
        int lineStart = content.LastIndexOf('\n', startMatch.Index);
        lineStart = lineStart < 0 ? startMatch.Index : lineStart + 1;

        var endPattern = @"#endregion";
        var endRegex = new Regex(endPattern);
        var endMatch = endRegex.Match(content, startMatch.Index);
        if (!endMatch.Success) return null;

        // Include the rest of the endregion line
        int lineEnd = content.IndexOf('\n', endMatch.Index);
        lineEnd = lineEnd < 0 ? content.Length : lineEnd + 1;

        return (lineStart, lineEnd);
    }

    private static string InsertInParentClass(string content, string parentName, string region)
    {
        // Find the parent class and insert the region before its closing brace
        var classPattern = $@"class\s+{Regex.Escape(parentName)}";
        var classMatch = Regex.Match(content, classPattern);
        if (!classMatch.Success) return content;

        // Find the opening brace of the class
        int openBrace = content.IndexOf('{', classMatch.Index);
        if (openBrace < 0) return content;

        // Find the matching closing brace
        int depth = 0;
        int closeBrace = -1;
        for (int i = openBrace; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    closeBrace = i;
                    break;
                }
            }
        }

        if (closeBrace < 0) return content;

        // Insert the region before the closing brace, with proper newlines
        var indent = DetectIndent(content, closeBrace);
        var regionWithIndent = "\n" + indent + region.Trim().Replace("\n", "\n" + indent) + "\n";

        return content[..closeBrace] + regionWithIndent + content[closeBrace..];
    }

    private static string DetectIndent(string content, int position)
    {
        // Walk back to find the indentation of the line containing position
        int lineStart = content.LastIndexOf('\n', position);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var line = content[lineStart..position];
        var indent = "";
        foreach (char c in line)
        {
            if (c == ' ' || c == '\t') indent += c;
            else break;
        }
        // Add one more level
        return indent + "    ";
    }
}

public enum SupportedLanguage
{
    CSharp,
    VB,
}
