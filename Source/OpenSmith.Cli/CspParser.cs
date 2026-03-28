using System.Xml.Linq;

namespace OpenSmith.Cli;

public static class CspParser
{
    private static readonly XNamespace Ns = "http://www.codesmithtools.com/schema/csp.xsd";

    public static CspProject Parse(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);
        var root = doc.Root!;

        var variables = new Dictionary<string, string>();
        var variablesEl = root.Element(Ns + "variables");
        if (variablesEl != null)
        {
            foreach (var add in variablesEl.Elements(Ns + "add"))
            {
                var key = add.Attribute("key")?.Value;
                var value = add.Attribute("value")?.Value;
                if (key != null && value != null)
                    variables[key] = value;
            }
        }

        var propertySets = new List<CspPropertySet>();
        var propertySetsEl = root.Element(Ns + "propertySets");
        if (propertySetsEl != null)
        {
            foreach (var ps in propertySetsEl.Elements(Ns + "propertySet"))
            {
                var propertySet = new CspPropertySet
                {
                    Name = ps.Attribute("name")?.Value ?? "",
                    OutputPath = ps.Attribute("output")?.Value,
                    TemplatePath = ps.Attribute("template")?.Value ?? "",
                };

                foreach (var prop in ps.Elements(Ns + "property"))
                {
                    var name = prop.Attribute("name")?.Value ?? "";
                    var cspProp = ParseProperty(prop, variables);
                    propertySet.Properties[name] = cspProp;
                }

                propertySets.Add(propertySet);
            }
        }

        return new CspProject { Variables = variables, PropertySets = propertySets };
    }

    private static CspProperty ParseProperty(XElement prop, Dictionary<string, string> variables)
    {
        var stringListEl = prop.Element(Ns + "stringList") ?? prop.Element("stringList");
        if (stringListEl != null)
        {
            var ns = stringListEl.Name.Namespace;
            var items = stringListEl.Elements(ns + "string")
                .Select(s => ResolveVariables(s.Value, variables))
                .ToList();
            return new CspProperty { StringList = items };
        }

        var connectionStringEl = prop.Element(Ns + "connectionString") ?? prop.Element("connectionString");
        if (connectionStringEl != null)
        {
            return new CspProperty { ComplexXml = prop };
        }

        // Check for other complex XML children (NamingProperty, etc.)
        var children = prop.Elements().Where(e =>
            e.Name.LocalName != "stringList" &&
            e.Name.LocalName != "connectionString" &&
            e.Name.LocalName != "providerType").ToList();

        if (children.Count > 0)
        {
            return new CspProperty { ComplexXml = prop };
        }

        // Simple text value
        return new CspProperty { Value = ResolveVariables(prop.Value.Trim(), variables) };
    }

    private static string ResolveVariables(string text, Dictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
        {
            text = text.Replace($"$({key})", value);
        }
        return text;
    }
}

public class CspProject
{
    public Dictionary<string, string> Variables { get; set; } = new();
    public List<CspPropertySet> PropertySets { get; set; } = new();
}

public class CspPropertySet
{
    public string Name { get; set; } = "";
    public string? OutputPath { get; set; }
    public string TemplatePath { get; set; } = "";
    public Dictionary<string, CspProperty> Properties { get; set; } = new();
}

public class CspProperty
{
    public string? Value { get; set; }
    public List<string>? StringList { get; set; }
    public XElement? ComplexXml { get; set; }
}
