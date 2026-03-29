using System.Text.RegularExpressions;
using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using OpenSmith.Compilation;
using OpenSmith.Engine;
using OpenSmith.Plinqo.Tests.Fixtures;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.EndToEnd;

[Collection("AdventureWorks")]
public class CodeGenerationTests : IDisposable
{
    private readonly DatabaseSchema _schema;
    private readonly AdventureWorksFixture _fixture;
    private readonly string _tempDir;

    public CodeGenerationTests(AdventureWorksFixture fixture)
    {
        _fixture = fixture;
        _schema = fixture.Schema;
        _tempDir = fixture.CreateTempDir();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string TemplatesDir =>
        Path.Combine(TestContext.PlinqoRoot, "src", "OpenSmith.Plinqo", "Templates");

    private Database GenerateDbml()
    {
        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, "AdventureWorks.dbml"),
            IncludeViews = false,
            IncludeFunctions = false,
            IncludeDeleteOnNull = true,
            EntityNaming = EntityNamingEnum.Preserve,
            TableNaming = TableNamingEnum.Mixed,
            AssociationNaming = AssociationNamingEnum.ListSuffix,
            EntityNamespace = "AdventureWorks.Data",
            ContextNamespace = "AdventureWorks.Data",
        };
        // Use a small subset for faster end-to-end testing
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.SalesOrderHeader$"));
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.SalesOrderDetail$"));
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.Customer$"));
        settings.IgnoreExpressions.Add(new Regex(@"sysdiagrams$"));

        var generator = new DbmlGenerator(settings);
        return generator.Create(_schema);
    }

    [Fact]
    public void EndToEnd_DbmlGeneration_ProducesDbmlFile()
    {
        GenerateDbml();

        var dbmlPath = Path.Combine(_tempDir, "AdventureWorks.dbml");
        Assert.True(File.Exists(dbmlPath), "DBML file should be produced");

        var content = File.ReadAllText(dbmlPath);
        Assert.Contains("<Table", content);
        Assert.Contains("SalesOrderHeader", content);
    }

    [Fact]
    public void EndToEnd_EntitiesTemplate_Compiles()
    {
        var templatePath = Path.Combine(TemplatesDir, "Entities.cst");
        if (!File.Exists(templatePath))
        {
            Assert.Fail($"Template not found: {templatePath}");
            return;
        }

        var registry = new TemplateRegistry();
        var rootClassName = registry.Resolve(templatePath);

        var generator = new TemplateCodeGenerator();
        var sources = new Dictionary<string, string>();
        var registeredTemplates = registry.Entries.ToDictionary(e => e.Key, e => e.Value.Parsed);

        foreach (var (className, entry) in registry.Entries)
        {
            sources[className] = generator.GenerateClass(className, entry.Parsed, registeredTemplates);
        }

        // Add Assembly Src files
        foreach (var entry in registry.Entries.Values)
        {
            foreach (var asm in entry.Parsed.Assemblies)
            {
                if (!string.IsNullOrEmpty(asm.Src))
                {
                    var srcPath = Path.GetFullPath(
                        Path.Combine(Path.GetDirectoryName(entry.AbsolutePath)!, asm.Src));
                    if (File.Exists(srcPath))
                    {
                        var srcContent = File.ReadAllText(srcPath);
                        srcContent = TemplateCompiler.PrepareInlineSource(srcContent);
                        var srcName = Path.GetFileNameWithoutExtension(srcPath);
                        sources.TryAdd(srcName, srcContent);
                    }
                }
            }
        }

        var compiler = new TemplateCompiler();
        var typeMap = compiler.Compile(sources);

        Assert.True(typeMap.ContainsKey(rootClassName),
            $"Expected root type '{rootClassName}' in compiled assembly");
        Assert.True(typeMap.Count >= 8,
            $"Expected at least 8 template types, got {typeMap.Count}");
    }

    [Fact]
    public void EndToEnd_EntitiesTemplate_GeneratesEntityFiles()
    {
        var database = GenerateDbml();

        var templatePath = Path.Combine(TemplatesDir, "Entities.cst");
        if (!File.Exists(templatePath))
        {
            Assert.Fail($"Template not found: {templatePath}");
            return;
        }

        // Build CSP XML for the Entities template
        var entitiesDir = Path.Combine(_tempDir, "Entities");
        Directory.CreateDirectory(entitiesDir);

        var cspXml = $"""
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <variables />
              <propertySets>
                <propertySet name="Entities" template="{templatePath}">
                  <property name="DbmlFile">{Path.Combine(_tempDir, "AdventureWorks.dbml")}</property>
                  <property name="Framework">v45</property>
                  <property name="IncludeDataServices">False</property>
                  <property name="IncludeDataRules">False</property>
                  <property name="AuditingEnabled">False</property>
                  <property name="IncludeDataContract">True</property>
                  <property name="IncludeXmlSerialization">False</property>
                  <property name="IncludeManyToMany">False</property>
                  <property name="AssociationNamingSuffix">ListSuffix</property>
                  <property name="OutputDirectory">{entitiesDir}</property>
                  <property name="BaseDirectory">{_tempDir}</property>
                  <property name="InterfaceDirectory" />
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var cspPath = Path.Combine(_tempDir, "Generate.csp");
        File.WriteAllText(cspPath, cspXml);

        var runner = new CspRunner(verbose: false, useCache: false);
        runner.Run(cspPath);

        // Verify entity files were generated
        var generatedFiles = Directory.GetFiles(entitiesDir, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(generatedFiles);

        // Should have files for each table in the DBML
        Assert.True(generatedFiles.Length >= database.Tables.Count,
            $"Expected at least {database.Tables.Count} entity files, got {generatedFiles.Length}");
    }

    [Fact]
    public void EndToEnd_DataContext_Generated()
    {
        GenerateDbml();

        var templatePath = Path.Combine(TemplatesDir, "Entities.cst");
        if (!File.Exists(templatePath))
        {
            Assert.Fail($"Template not found: {templatePath}");
            return;
        }

        var entitiesDir = Path.Combine(_tempDir, "Entities");
        Directory.CreateDirectory(entitiesDir);

        var cspXml = $"""
            <?xml version="1.0" encoding="utf-8" ?>
            <codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
              <variables />
              <propertySets>
                <propertySet name="Entities" template="{templatePath}">
                  <property name="DbmlFile">{Path.Combine(_tempDir, "AdventureWorks.dbml")}</property>
                  <property name="Framework">v45</property>
                  <property name="IncludeDataServices">False</property>
                  <property name="IncludeDataRules">False</property>
                  <property name="AuditingEnabled">False</property>
                  <property name="IncludeDataContract">True</property>
                  <property name="IncludeXmlSerialization">False</property>
                  <property name="IncludeManyToMany">False</property>
                  <property name="AssociationNamingSuffix">ListSuffix</property>
                  <property name="OutputDirectory">{entitiesDir}</property>
                  <property name="BaseDirectory">{_tempDir}</property>
                  <property name="InterfaceDirectory" />
                </propertySet>
              </propertySets>
            </codeSmith>
            """;

        var cspPath = Path.Combine(_tempDir, "GenerateCtx.csp");
        File.WriteAllText(cspPath, cspXml);

        var runner = new CspRunner(verbose: false, useCache: false);
        runner.Run(cspPath);

        // DataContext files are written to BaseDirectory
        var dataContextFiles = Directory.GetFiles(_tempDir, "*DataContext*", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(dataContextFiles);
    }
}
