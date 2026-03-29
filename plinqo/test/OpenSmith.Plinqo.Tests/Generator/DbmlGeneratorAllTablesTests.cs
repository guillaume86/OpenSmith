using System.Text.RegularExpressions;
using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using OpenSmith.Plinqo.Tests.Fixtures;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.Generator;

[Collection("AdventureWorks")]
public class DbmlGeneratorAllTablesTests : IDisposable
{
    private readonly DatabaseSchema _schema;
    private readonly string _tempDir;
    private readonly Database _database;

    public DbmlGeneratorAllTablesTests(AdventureWorksFixture fixture)
    {
        _schema = fixture.Schema;
        _tempDir = fixture.CreateTempDir();

        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, "AdventureWorks.dbml"),
            IncludeViews = true,
            IncludeFunctions = true,
            IncludeDeleteOnNull = true,
            EntityNaming = EntityNamingEnum.Preserve,
            TableNaming = TableNamingEnum.Mixed,
            AssociationNaming = AssociationNamingEnum.ListSuffix,
            EntityNamespace = "AdventureWorks.Data",
            ContextNamespace = "AdventureWorks.Data",
        };
        settings.IncludeExpressions.Add(new Regex(".*"));
        settings.IgnoreExpressions.Add(new Regex(@"sysdiagrams$"));

        var generator = new DbmlGenerator(settings);
        _database = generator.Create(_schema);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void AllTables_IncludesAllNonIgnoredTables()
    {
        // DBML tables include both database tables and views (IncludeViews=true)
        var expectedTableCount = _schema.Tables.Count(t => !t.FullName.Contains("sysdiagrams"));
        var expectedViewCount = _schema.Views.Count(v => !v.FullName.Contains("sysdiagrams"));
        var expectedTotal = expectedTableCount + expectedViewCount;
        // Some tables may become enums, so allow a range
        Assert.InRange(_database.Tables.Count, expectedTotal - 10, expectedTotal);
    }

    [Fact]
    public void AllTables_HasAssociations()
    {
        var totalAssociations = _database.Tables.Sum(t => t.Type.Associations.Count);
        // AdventureWorks has many FK relationships
        Assert.True(totalAssociations > 50, $"Expected >50 associations, got {totalAssociations}");
    }

    [Fact]
    public void AllTables_EachTableHasColumns()
    {
        foreach (var table in _database.Tables)
        {
            Assert.NotEmpty(table.Type.Columns);
        }
    }

    [Fact]
    public void AllTables_MultiSchemaNames_Preserved()
    {
        var tableNames = _database.Tables.Select(t => t.Name).ToList();

        // Tables from different schemas should be present
        Assert.Contains(tableNames, n => n.StartsWith("Person."));
        Assert.Contains(tableNames, n => n.StartsWith("Sales."));
        Assert.Contains(tableNames, n => n.StartsWith("Production."));
    }

    [Fact]
    public void AllTables_DbmlFileWritten()
    {
        var dbmlPath = Path.Combine(_tempDir, "AdventureWorks.dbml");
        Assert.True(File.Exists(dbmlPath), "DBML file should be written to disk");

        var content = File.ReadAllText(dbmlPath);
        Assert.Contains("<Database", content);
        Assert.Contains("<Table", content);
    }

    [Fact]
    public void AllTables_DbmlRoundTrips()
    {
        var dbmlPath = Path.Combine(_tempDir, "AdventureWorks.dbml");
        var roundTripped = Dbml.FromFile(dbmlPath);

        Assert.Equal(_database.Tables.Count, roundTripped.Tables.Count);
    }

    [Fact]
    public void AllTables_ContextNamespaceSet()
    {
        Assert.Equal("AdventureWorks.Data", _database.ContextNamespace);
    }

    [Fact]
    public void AllTables_EntityNamespaceSet()
    {
        Assert.Equal("AdventureWorks.Data", _database.EntityNamespace);
    }

    [Fact]
    public void AllTables_ForeignKeyAssociations_PairedCorrectly()
    {
        // Each FK association should have a corresponding PK-side association
        foreach (var table in _database.Tables)
        {
            foreach (var assoc in table.Type.Associations)
            {
                if (assoc.IsForeignKey == true)
                {
                    var otherTable = _database.Tables.FirstOrDefault(t => t.Type.Name == assoc.Type);
                    Assert.NotNull(otherTable);

                    var pkSide = otherTable.Type.Associations
                        .FirstOrDefault(a => a.Name == assoc.Name && a.IsForeignKey != true);
                    Assert.NotNull(pkSide);
                }
            }
        }
    }
}
