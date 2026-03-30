using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using System.Xml.Serialization;
using OpenSmith.Engine;

namespace OpenSmith.Compilation;

/// <summary>
/// Deserializes CspProperty values from the CSP XML into CLR objects
/// and assigns them to compiled template instances via reflection.
/// </summary>
public static class PropertyDeserializer
{
    private static AssemblyLoadContext? _loadContext;

    /// <summary>
    /// Sets the AssemblyLoadContext used to resolve provider assemblies at runtime.
    /// </summary>
    public static void SetLoadContext(AssemblyLoadContext alc) => _loadContext = alc;

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

        // Complex XML (provider-based or XmlSerializer)
        if (cspProp.ComplexXml != null)
            return DeserializeComplexXml(targetType, cspProp, variables);

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

    private static object? DeserializeComplexXml(Type targetType, CspProperty cspProp, Dictionary<string, string> variables)
    {
        var xml = cspProp.ComplexXml!;
        var ns = xml.Name.Namespace;

        // Convention: if the XML contains a <providerType> element, resolve it dynamically.
        // The CSP format is: <connectionString>...</connectionString><providerType>Type,Assembly</providerType>
        var providerTypeEl = xml.Element(ns + "providerType") ?? xml.Element("providerType");
        if (providerTypeEl != null)
        {
            var connStrEl = xml.Element(ns + "connectionString") ?? xml.Element("connectionString");
            if (connStrEl != null)
            {
                var connStr = ResolveVariables(connStrEl.Value, variables);
                return InvokeSchemaProvider(providerTypeEl.Value, connStr, targetType, cspProp.ProviderHints);
            }
            return null;
        }

        // Fallback: XmlSerializer for types whose root element name matches a child element
        // e.g. <NamingProperty>...</NamingProperty> inside the property XML
        var childEl = xml.Elements().FirstOrDefault(e => e.Name.LocalName == targetType.Name);
        if (childEl != null)
        {
            var serializer = new XmlSerializer(targetType);
            using var reader = childEl.CreateReader();
            return serializer.Deserialize(reader);
        }

        // Last resort: try XmlSerializer on the element itself
        try
        {
            var serializer = new XmlSerializer(targetType);
            using var reader = xml.CreateReader();
            return serializer.Deserialize(reader);
        }
        catch
        {
            return Activator.CreateInstance(targetType);
        }
    }

    /// <summary>
    /// Resolves a provider type string (e.g. "SchemaExplorer.SqlSchemaProvider,OpenSmith.SchemaExplorer")
    /// and invokes GetDatabaseSchema(connectionString) via reflection.
    /// Provider hints (DeepLoad, IncludeViews, IncludeFunctions) from the template's <%@ Property %>
    /// directive are set on the provider instance before invocation.
    /// </summary>
    private static object? InvokeSchemaProvider(string providerTypeString, string connectionString,
        Type targetType, Dictionary<string, bool>? providerHints)
    {
        // Parse "TypeName,AssemblyName" format
        var parts = providerTypeString.Split(',', 2, StringSplitOptions.TrimEntries);
        var typeName = parts[0];

        // Try to find the type in already-loaded assemblies
        Type? providerType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            providerType = asm.GetType(typeName);
            if (providerType != null) break;
        }

        // If not found, load the assembly via the template ALC (which resolves from the publish directory)
        if (providerType == null && parts.Length > 1 && _loadContext != null)
        {
            var assemblyName = parts[1];
            try
            {
                var loaded = _loadContext.LoadFromAssemblyName(new AssemblyName(assemblyName));
                providerType = loaded.GetType(typeName);
            }
            catch (FileNotFoundException) { }
        }

        if (providerType == null)
            throw new InvalidOperationException($"Could not resolve provider type '{typeName}'. Ensure the assembly containing it is referenced.");

        var instance = Activator.CreateInstance(providerType);

        // Apply provider hints as properties on the instance (e.g. DeepLoad, IncludeViews, IncludeFunctions)
        if (providerHints != null)
        {
            foreach (var (hintName, hintValue) in providerHints)
            {
                var hintProp = providerType.GetProperty(hintName, BindingFlags.Public | BindingFlags.Instance);
                if (hintProp != null && hintProp.CanWrite && hintProp.PropertyType == typeof(bool))
                    hintProp.SetValue(instance, hintValue);
            }
        }

        var method = providerType.GetMethod("GetDatabaseSchema", [typeof(string)]);
        if (method == null)
            throw new InvalidOperationException($"Provider type '{typeName}' does not have a GetDatabaseSchema(string) method.");

        try
        {
            return method.Invoke(instance, [connectionString]);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static string ResolveVariables(string text, Dictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
            text = text.Replace($"$({key})", value);
        return text;
    }
}
