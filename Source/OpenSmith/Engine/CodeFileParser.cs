using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace OpenSmith.Engine;

/// <summary>
/// Parses an existing C# file to extract attribute sections from a named inner class.
/// This is used to preserve user-applied attributes when regenerating editable partial classes.
/// Replaces the original ICSharpCode.NRefactory-based CodeFileParser.
/// </summary>
public class CodeFileParser
{
    private readonly string[] _lines;
    private readonly string _content;

    public CompilationUnit CompilationUnit { get; }

    public CodeFileParser(string filePath)
    {
        _content = File.ReadAllText(filePath);
        _lines = File.ReadAllLines(filePath);
        CompilationUnit = new CompilationUnit(_content, _lines);
    }

    /// <summary>
    /// Extracts a section of text from the source file between the given locations.
    /// </summary>
    public string GetSection(SourceLocation start, SourceLocation end)
    {
        if (start.Line < 1 || end.Line < 1 || start.Line > _lines.Length || end.Line > _lines.Length)
            return string.Empty;

        if (start.Line == end.Line)
        {
            var line = _lines[start.Line - 1];
            int startCol = Math.Min(start.Column - 1, line.Length);
            int endCol = Math.Min(end.Column - 1, line.Length);
            return line[startCol..endCol];
        }

        var result = new System.Text.StringBuilder();
        // First line
        var firstLine = _lines[start.Line - 1];
        int firstStart = Math.Min(start.Column - 1, firstLine.Length);
        result.AppendLine(firstLine[firstStart..]);

        // Middle lines
        for (int i = start.Line; i < end.Line - 1; i++)
            result.AppendLine(_lines[i]);

        // Last line
        var lastLine = _lines[end.Line - 1];
        int lastEnd = Math.Min(end.Column - 1, lastLine.Length);
        result.Append(lastLine[..lastEnd]);

        return result.ToString();
    }
}

/// <summary>
/// Represents a source code location (1-based line and column).
/// </summary>
public struct SourceLocation
{
    public int Line { get; set; }
    public int Column { get; set; }

    public SourceLocation(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

/// <summary>
/// Represents a parsed C# compilation unit.
/// </summary>
public class CompilationUnit
{
    private readonly string _content;
    private readonly string[] _lines;

    public CompilationUnit(string content, string[] lines)
    {
        _content = content;
        _lines = lines;
    }

    /// <summary>
    /// Accepts a visitor that scans for attributes within a named class section.
    /// </summary>
    public void AcceptVisitor(AttributeSectionVisitor visitor, string className)
    {
        visitor.Visit(_content, _lines, className);
    }
}

/// <summary>
/// Visits a C# file to extract attribute sections from properties within a named class.
/// </summary>
public class AttributeSectionVisitor
{
    public Dictionary<string, PropertyAttributes> PropertyMap { get; } = new(StringComparer.Ordinal);

    private static readonly Regex ClassRegex = new(
        @"(?:class|struct)\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex PropertyRegex = new(
        @"^\s*public\s+\S+.*?\s+(\w+)\s*\{\s*get\s*;",
        RegexOptions.Compiled);

    private static readonly Regex AttributeRegex = new(
        @"^\s*\[(.+)\]\s*$",
        RegexOptions.Compiled);

    public void Visit(string content, string[] lines, string targetClassName)
    {
        PropertyMap.Clear();

        // Find the target class
        bool inTargetClass = false;
        int braceDepth = 0;
        int classStartBraceDepth = 0;

        var pendingAttributes = new List<AttributeSection>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            if (!inTargetClass)
            {
                // Look for the target class
                var classMatch = ClassRegex.Match(line);
                if (classMatch.Success && classMatch.Groups[1].Value == targetClassName)
                {
                    inTargetClass = true;
                    classStartBraceDepth = braceDepth;
                    // Count braces on this line too
                    foreach (char c in line)
                    {
                        if (c == '{') braceDepth++;
                        else if (c == '}') braceDepth--;
                    }
                    continue;
                }
                foreach (char c in line)
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}') braceDepth--;
                }
                continue;
            }

            // We're inside the target class
            // Check for attributes
            var attrMatch = AttributeRegex.Match(line);
            if (attrMatch.Success)
            {
                pendingAttributes.Add(new AttributeSection
                {
                    Text = trimmed,
                    StartLocation = new SourceLocation(i + 1, line.Length - line.TrimStart().Length + 1),
                    EndLocation = new SourceLocation(i + 1, line.TrimEnd().Length + 1),
                });
            }
            else
            {
                // Check for property declaration
                var propMatch = PropertyRegex.Match(line);
                if (propMatch.Success)
                {
                    string propName = propMatch.Groups[1].Value;
                    if (pendingAttributes.Count > 0)
                    {
                        PropertyMap[propName] = new PropertyAttributes
                        {
                            Attributes = new List<AttributeSection>(pendingAttributes)
                        };
                    }
                    pendingAttributes.Clear();
                }
                else if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("//"))
                {
                    // Non-attribute, non-property line — clear pending
                    pendingAttributes.Clear();
                }
            }

            // Track brace depth
            foreach (char c in line)
            {
                if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
            }

            // Check if we've exited the target class
            if (braceDepth <= classStartBraceDepth)
                break;
        }
    }
}

/// <summary>
/// Holds attribute sections for a property.
/// </summary>
public class PropertyAttributes
{
    public List<AttributeSection> Attributes { get; set; } = new();
}

/// <summary>
/// Represents a single [Attribute] section with its source location.
/// </summary>
public class AttributeSection
{
    public string Text { get; set; }
    public SourceLocation StartLocation { get; set; }
    public SourceLocation EndLocation { get; set; }
}
