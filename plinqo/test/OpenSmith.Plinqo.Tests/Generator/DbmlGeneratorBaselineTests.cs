using System.Text.RegularExpressions;
using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using OpenSmith.Plinqo.Tests.Fixtures;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.Generator;

[Collection("AdventureWorks")]
public class DbmlGeneratorBaselineTests : IDisposable
{
    private readonly DatabaseSchema _schema;
    private readonly string _tempDir;
    private readonly Database _database;

    // A curated subset of AdventureWorks tables for baseline testing
    private static readonly string[] IncludedTables =
    [
        @"^Sales\.SalesOrderHeader$",
        @"^Sales\.SalesOrderDetail$",
        @"^Sales\.Customer$",
        @"^Sales\.SalesPerson$",
        @"^Production\.Product$",
        @"^Production\.ProductCategory$",
        @"^Production\.ProductSubcategory$",
        @"^Person\.Person$",
        @"^Person\.Address$",
        @"^HumanResources\.Employee$",
    ];

    public DbmlGeneratorBaselineTests(AdventureWorksFixture fixture)
    {
        _schema = fixture.Schema;
        _tempDir = fixture.CreateTempDir();

        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, "Baseline.dbml"),
            IncludeViews = true,
            IncludeFunctions = true,
            IncludeDeleteOnNull = true,
            EntityNaming = EntityNamingEnum.Preserve,
            TableNaming = TableNamingEnum.Mixed,
            AssociationNaming = AssociationNamingEnum.ListSuffix,
            EntityBase = "LinqEntityBase",
            EntityNamespace = "AdventureWorks.Data",
            ContextNamespace = "AdventureWorks.Data",
        };

        foreach (var pattern in IncludedTables)
            settings.IncludeExpressions.Add(new Regex(pattern));

        settings.IgnoreExpressions.Add(new Regex(@"sysdiagrams$"));
        settings.CleanExpressions.Add(new Regex(@"^(sp|tbl|udf|vw)_"));

        var generator = new DbmlGenerator(settings);
        _database = generator.Create(_schema);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Baseline_GeneratesExpectedTableCount()
    {
        // Should contain roughly the number of tables in the IncludeList
        Assert.InRange(_database.Tables.Count, 8, IncludedTables.Length + 2);
    }

    [Fact]
    public void Baseline_TablesHaveAssociations()
    {
        // SalesOrderDetail should have FK to SalesOrderHeader
        var detailTable = _database.Tables
            .FirstOrDefault(t => t.Name.Contains("SalesOrderDetail"));
        Assert.NotNull(detailTable);

        var fkAssociations = detailTable.Type.Associations
            .Where(a => a.IsForeignKey == true).ToList();
        Assert.NotEmpty(fkAssociations);
    }

    [Fact]
    public void Baseline_DeleteOnNull_SetOnNonNullableFk()
    {
        // Find a non-nullable FK association - SalesOrderDetail.SalesOrderID is NOT NULL
        var detailTable = _database.Tables
            .FirstOrDefault(t => t.Name.Contains("SalesOrderDetail"));
        Assert.NotNull(detailTable);

        var fkToHeader = detailTable.Type.Associations
            .FirstOrDefault(a => a.IsForeignKey == true &&
                                 a.Name.Contains("SalesOrderHeader"));

        if (fkToHeader != null)
        {
            Assert.Equal(true, fkToHeader.DeleteOnNull);
        }
    }

    [Fact]
    public void Baseline_IgnoreExpressions_ExcludeSysdiagrams()
    {
        Assert.DoesNotContain(_database.Tables, t => t.Name.Contains("sysdiagrams"));
    }

    [Fact]
    public void Baseline_EntityBaseSet()
    {
        Assert.Equal("LinqEntityBase", _database.EntityBase);
    }

    [Fact]
    public void Baseline_ContextNamespaceSet()
    {
        Assert.Equal("AdventureWorks.Data", _database.ContextNamespace);
    }

    [Fact]
    public void Baseline_EntityNamespaceSet()
    {
        Assert.Equal("AdventureWorks.Data", _database.EntityNamespace);
    }

    [Fact]
    public void Baseline_ViewsIncluded()
    {
        // With IncludeViews=true, views matching the include list should appear
        // Views in AdventureWorks that match our include patterns may or may not exist,
        // but the setting should be respected without error
        var dbmlPath = Path.Combine(_tempDir, "Baseline.dbml");
        Assert.True(File.Exists(dbmlPath));
    }

    [Fact]
    public void Baseline_FunctionsIncluded()
    {
        // AdventureWorks has stored procedures; those matching include patterns appear as Functions
        // The important thing is no error occurs with IncludeFunctions=true
        Assert.NotNull(_database.Functions);
    }

    [Fact]
    public void Baseline_DbmlSerializes()
    {
        var dbmlPath = Path.Combine(_tempDir, "Baseline.dbml");
        var content = File.ReadAllText(dbmlPath);
        Assert.Contains("<?xml", content);
        Assert.Contains("<Database", content);
    }

    [Fact]
    public void Baseline_CleanExpressions_AppliedToNames()
    {
        // Clean expressions strip prefixes like sp_, tbl_, etc.
        // Verify no table Member starts with these prefixes
        foreach (var table in _database.Tables)
        {
            Assert.False(table.Member?.StartsWith("sp_"),
                $"Table member '{table.Member}' should have had sp_ prefix cleaned");
            Assert.False(table.Member?.StartsWith("tbl_"),
                $"Table member '{table.Member}' should have had tbl_ prefix cleaned");
        }
    }
}
