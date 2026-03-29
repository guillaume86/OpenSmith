using System.Text.RegularExpressions;
using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using OpenSmith.Plinqo.Tests.Fixtures;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.Generator;

[Collection("AdventureWorks")]
public class DbmlGeneratorTablesOnlyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Database _database;

    public DbmlGeneratorTablesOnlyTests(AdventureWorksFixture fixture)
    {
        _tempDir = fixture.CreateTempDir();

        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, "TablesOnly.dbml"),
            IncludeViews = false,
            IncludeFunctions = false,
            IncludeDeleteOnNull = true,
            EntityNaming = EntityNamingEnum.Preserve,
            TableNaming = TableNamingEnum.Mixed,
            AssociationNaming = AssociationNamingEnum.ListSuffix,
        };
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.SalesOrderHeader$"));
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.SalesOrderDetail$"));
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.Customer$"));
        settings.IncludeExpressions.Add(new Regex(@"^Production\.Product$"));
        settings.IgnoreExpressions.Add(new Regex(@"sysdiagrams$"));

        var generator = new DbmlGenerator(settings);
        _database = generator.Create(fixture.Schema);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void TablesOnly_NoFunctionsInOutput()
    {
        Assert.Empty(_database.Functions);
    }

    [Fact]
    public void TablesOnly_TablesStillPresent()
    {
        Assert.NotEmpty(_database.Tables);
        Assert.InRange(_database.Tables.Count, 3, 5);
    }

    [Fact]
    public void TablesOnly_AssociationsStillPresent()
    {
        // SalesOrderDetail FK to SalesOrderHeader should still work
        var detailTable = _database.Tables
            .FirstOrDefault(t => t.Name.Contains("SalesOrderDetail"));
        Assert.NotNull(detailTable);
        Assert.NotEmpty(detailTable.Type.Associations);
    }

    [Fact]
    public void TablesOnly_NoViewTables()
    {
        // With IncludeViews=false, no view-based tables should appear
        // Views in AdventureWorks start with "v" prefix typically
        // More importantly, the table count should match only actual tables from include list
        foreach (var table in _database.Tables)
        {
            // All tables should come from our include list patterns
            Assert.True(
                table.Name.Contains("SalesOrderHeader") ||
                table.Name.Contains("SalesOrderDetail") ||
                table.Name.Contains("Customer") ||
                table.Name.Contains("Product"),
                $"Unexpected table '{table.Name}' — should only contain tables from include list");
        }
    }
}
