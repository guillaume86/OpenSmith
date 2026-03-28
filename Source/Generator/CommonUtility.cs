using System.Text;
using System.Text.RegularExpressions;
using OpenSmith.Engine;

namespace LinqToSqlShared.Generator;

public static class CommonUtility
{
    public const string TrueLiteral = "true";
    public const string FalseLiteral = "false";
    private static readonly Regex _sizeRegex = new(@"(?<Size>\d+)", RegexOptions.Compiled);

    public static bool IsNullableType(string nativeType)
    {
        if (nativeType.StartsWith("System."))
        {
            var myType = System.Type.GetType(nativeType, false);
            if (myType != null)
                return myType.IsValueType;
        }
        return false;
    }

    public static string GetFullName(string classNamespace, string className) =>
        $"{classNamespace}.{className}";

    public static string GetClassName(string name)
    {
        if (!name.Contains('.'))
            return name;

        string[] namespaces = name.Split('.');
        return namespaces[^1];
    }

    public static string GetNamespace(string name)
    {
        if (!name.Contains('.'))
            return name;

        string[] namespaces = name.Split('.');
        return string.Join(".", namespaces, 0, namespaces.Length - 1);
    }

    public static string GetFieldName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentNullException(nameof(propertyName));

        return propertyName.Length > 1
            ? $"_{propertyName[..1].ToLowerInvariant()}{propertyName[1..]}"
            : $"_{propertyName}";
    }

    public static string GetParameterName(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        return name.Length > 1
            ? name[..1].ToLowerInvariant() + name[1..]
            : name[..1].ToLowerInvariant();
    }

    public static string ToBooleanString(bool value) =>
        value ? TrueLiteral : FalseLiteral;

    public static string ToSpaced(string name)
    {
        StringBuilder sb = new();
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && sb.Length != 0)
                sb.Append(' ');

            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    public static int GetColumnSize(string dbType)
    {
        int size = 0;

        var m = _sizeRegex.Match(dbType);
        if (!m.Success)
            return size;

        string temp = m.Groups["Size"].Value;
        int.TryParse(temp, out size);
        return size;
    }
}
