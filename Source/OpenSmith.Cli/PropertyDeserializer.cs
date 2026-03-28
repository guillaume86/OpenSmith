using System.Reflection;
using System.Xml.Linq;
using System.Xml.Serialization;
using LinqToSqlShared.Generator;
using OpenSmith.Engine;
using SchemaExplorer;

namespace OpenSmith.Cli;

/// <summary>
/// Deserializes CspProperty values from the CSP XML into CLR objects
/// and assigns them to compiled template instances via reflection.
/// </summary>
public static class PropertyDeserializer
{
    public static void SetProperties(
        CodeTemplateBase template,
        Dictionary<string, CspProperty> cspProperties,
        Dictionary<string, string> variables)
    {
        var type = template.GetType();

        foreach (var (name, cspProp) in cspProperties)
        {
            var propInfo = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (propInfo == null || !propInfo.CanWrite)
                continue;

            var value = DeserializeValue(propInfo.PropertyType, cspProp, variables);
            if (value != null)
                propInfo.SetValue(template, value);
        }
    }

    private static object? DeserializeValue(Type targetType, CspProperty cspProp, Dictionary<string, string> variables)
    {
        // String list
        if (cspProp.StringList != null)
        {
            if (targetType == typeof(List<string>))
                return cspProp.StringList;
            return cspProp.StringList;
        }

        // Complex XML (DatabaseSchema, NamingProperty, etc.)
        if (cspProp.ComplexXml != null)
            return DeserializeComplexXml(targetType, cspProp.ComplexXml, variables);

        // Simple value
        var text = cspProp.Value ?? "";

        if (targetType == typeof(string))
            return text;

        if (targetType == typeof(bool))
            return bool.TryParse(text, out var b) && b;

        if (targetType.IsEnum)
            return Enum.Parse(targetType, text, ignoreCase: true);

        if (targetType == typeof(int))
            return int.TryParse(text, out var i) ? i : 0;

        return text;
    }

    private static object? DeserializeComplexXml(Type targetType, XElement xml, Dictionary<string, string> variables)
    {
        // DatabaseSchema: <connectionString>...</connectionString><providerType>...</providerType>
        if (targetType == typeof(DatabaseSchema))
        {
            var ns = xml.Name.Namespace;
            var connStrEl = xml.Element(ns + "connectionString") ?? xml.Element("connectionString");
            if (connStrEl != null)
            {
                var connStr = ResolveVariables(connStrEl.Value, variables);
                var provider = new SqlSchemaProvider();
                return provider.GetDatabaseSchema(connStr);
            }
            return null;
        }

        // NamingProperty: XML serialized
        if (targetType == typeof(NamingProperty))
        {
            var namingEl = xml.Elements().FirstOrDefault(e => e.Name.LocalName == "NamingProperty");
            if (namingEl != null)
            {
                var serializer = new XmlSerializer(typeof(NamingProperty));
                using var reader = namingEl.CreateReader();
                return serializer.Deserialize(reader) as NamingProperty;
            }
            return new NamingProperty();
        }

        return null;
    }

    private static string ResolveVariables(string text, Dictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
            text = text.Replace($"$({key})", value);
        return text;
    }
}
