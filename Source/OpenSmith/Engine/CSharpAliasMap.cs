using System.Collections.Generic;

namespace OpenSmith.Engine;

/// <summary>
/// Maps .NET system type names to C# language aliases.
/// Equivalent to CodeSmith's System-CSharpAlias.csmap.
/// </summary>
public class CSharpAliasMap
{
    public static readonly CSharpAliasMap Instance = new();

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Decimal"] = "decimal",
        ["System.Double"] = "double",
        ["System.Single"] = "float",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.String"] = "string",
        ["System.Object"] = "object",
        ["System.Void"] = "void",
        ["System.Byte[]"] = "byte[]",
    };

    public string this[string typeName]
    {
        get => Aliases.TryGetValue(typeName, out var alias) ? alias : typeName;
    }
}
