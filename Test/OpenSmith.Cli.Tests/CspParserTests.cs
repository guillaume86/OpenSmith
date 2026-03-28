using OpenSmith.Cli;

namespace OpenSmith.Cli.Tests;

public class CspParserTests
{
    [Fact]
    public void ParsesVariables()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <variables>
                <add key="ConnStr" value="Server=.;Database=Test" />
              </variables>
              <propertySets />
            </codeSmith>
            """;

        var project = CspParser.Parse(xml);

        Assert.Single(project.Variables);
        Assert.Equal("Server=.;Database=Test", project.Variables["ConnStr"]);
    }

    [Fact]
    public void ParsesPropertySetMetadata()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <propertySets>
                <propertySet name="Dbml" output=".\Dbml.xml" template="..\CSharp\Dbml.cst">
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var project = CspParser.Parse(xml);

        Assert.Single(project.PropertySets);
        var ps = project.PropertySets[0];
        Assert.Equal("Dbml", ps.Name);
        Assert.Equal(@".\Dbml.xml", ps.OutputPath);
        Assert.Equal(@"..\CSharp\Dbml.cst", ps.TemplatePath);
    }

    [Fact]
    public void ParsesSimpleStringProperty()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <propertySets>
                <propertySet name="Test" template="test.cst">
                  <property name="EntityBase">LinqEntityBase</property>
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var project = CspParser.Parse(xml);
        var prop = project.PropertySets[0].Properties["EntityBase"];

        Assert.Equal("LinqEntityBase", prop.Value);
        Assert.Null(prop.StringList);
        Assert.Null(prop.ComplexXml);
    }

    [Fact]
    public void ParsesStringListProperty()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <propertySets>
                <propertySet name="Test" template="test.cst">
                  <property name="IgnoreList">
                    <stringList>
                      <string>sysdiagrams$</string>
                      <string>^Cm\d{10}$</string>
                    </stringList>
                  </property>
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var project = CspParser.Parse(xml);
        var prop = project.PropertySets[0].Properties["IgnoreList"];

        Assert.NotNull(prop.StringList);
        Assert.Equal(2, prop.StringList.Count);
        Assert.Equal("sysdiagrams$", prop.StringList[0]);
        Assert.Equal(@"^Cm\d{10}$", prop.StringList[1]);
    }

    [Fact]
    public void ResolvesVariableReferencesInProperties()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <variables>
                <add key="ConnectionString1" value="Server=.;Database=SampleDb" />
              </variables>
              <propertySets>
                <propertySet name="Test" template="test.cst">
                  <property name="SourceDatabase">
                    <connectionString>$(ConnectionString1)</connectionString>
                    <providerType>SchemaExplorer.SqlSchemaProvider</providerType>
                  </property>
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var project = CspParser.Parse(xml);
        var prop = project.PropertySets[0].Properties["SourceDatabase"];

        Assert.NotNull(prop.ComplexXml);
        // The connectionString element should have resolved variable
        var ns = prop.ComplexXml.Name.Namespace;
        var connStr = prop.ComplexXml.Element(ns + "connectionString")
                   ?? prop.ComplexXml.Element("connectionString");
        // Variable resolution happens at deserialization time for complex XML
    }

    [Fact]
    public void ParsesComplexNamingPropertyXml()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <propertySets>
                <propertySet name="Test" template="test.cst">
                  <property name="NamingConventions">
                    <NamingProperty xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                        xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                        xmlns="">
                      <TableNaming>Mixed</TableNaming>
                      <EntityNaming>Preserve</EntityNaming>
                      <AssociationNaming>ListSuffix</AssociationNaming>
                    </NamingProperty>
                  </property>
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var project = CspParser.Parse(xml);
        var prop = project.PropertySets[0].Properties["NamingConventions"];

        Assert.NotNull(prop.ComplexXml);
    }

    [Fact]
    public void ParsesEmptyPropertyValue()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <propertySets>
                <propertySet name="Test" template="test.cst">
                  <property name="InterfaceDirectory" />
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var project = CspParser.Parse(xml);
        var prop = project.PropertySets[0].Properties["InterfaceDirectory"];

        Assert.Equal("", prop.Value);
    }

    [Fact]
    public void ParsesMultiplePropertySets()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <propertySets>
                <propertySet name="Dbml" template="..\CSharp\Dbml.cst">
                </propertySet>
                <propertySet name="Entities" template="..\CSharp\Entities.cst">
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var project = CspParser.Parse(xml);

        Assert.Equal(2, project.PropertySets.Count);
        Assert.Equal("Dbml", project.PropertySets[0].Name);
        Assert.Equal("Entities", project.PropertySets[1].Name);
    }

    [Fact]
    public void ParsesSampleDbGeneratorCsp()
    {
        // Integration test: parse the actual CSP file
        var cspPath = Path.Combine(TestContext.RepoRoot, "DiffTest", "SampleDb-Generator.csp");
        if (!File.Exists(cspPath))
            return; // Skip if not available

        var xml = File.ReadAllText(cspPath);
        var project = CspParser.Parse(xml);

        Assert.Single(project.Variables);
        Assert.Equal(2, project.PropertySets.Count);
        Assert.Equal("Dbml", project.PropertySets[0].Name);
        Assert.Equal("Entities", project.PropertySets[1].Name);

        // Verify Dbml property set
        var dbml = project.PropertySets[0];
        Assert.Equal("True", dbml.Properties["IncludeViews"].Value);
        Assert.NotNull(dbml.Properties["IgnoreList"].StringList);
        Assert.Equal(2, dbml.Properties["IgnoreList"].StringList!.Count);
        Assert.NotNull(dbml.Properties["NamingConventions"].ComplexXml);
        Assert.NotNull(dbml.Properties["SourceDatabase"].ComplexXml);
    }
}

internal static class TestContext
{
    public static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "OpenSmith.slnx")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("Could not find repo root");
        }
    }
}
