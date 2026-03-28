using System.Collections.Generic;

namespace OpenSmith.Engine;

/// <summary>
/// Escapes C# reserved keywords by prepending @.
/// Equivalent to CodeSmith's CSharpKeyWordEscape.csmap.
/// </summary>
public class CSharpKeyWordEscapeMap
{
    public static readonly CSharpKeyWordEscapeMap Instance = new();

    private static readonly HashSet<string> Keywords = new()
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator",
        "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while",
    };

    public string this[string name]
    {
        get => Keywords.Contains(name) ? "@" + name : name;
    }
}
