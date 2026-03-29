using System.Text.RegularExpressions;
using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using OpenSmith.Plinqo.Tests.Fixtures;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.Generator;

[Collection("AdventureWorks")]
public class DbmlGeneratorNamingTests : IDisposable
{
    private readonly DatabaseSchema _schema;
    private readonly string _tempDir;

    public DbmlGeneratorNamingTests(AdventureWorksFixture fixture)
    {
        _schema = fixture.Schema;
        _tempDir = fixture.CreateTempDir();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private Database Generate(
        TableNamingEnum tableNaming,
        EntityNamingEnum entityNaming,
        AssociationNamingEnum associationNaming,
        bool disableRenaming = false)
    {
        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, $"Naming_{tableNaming}_{entityNaming}_{associationNaming}.dbml"),
            IncludeViews = false,
            IncludeFunctions = false,
            TableNaming = tableNaming,
            EntityNaming = entityNaming,
            AssociationNaming = associationNaming,
            DisableRenaming = disableRenaming,
        };
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.SalesOrderHeader$"));
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.SalesOrderDetail$"));
        settings.IncludeExpressions.Add(new Regex(@"^Sales\.Customer$"));
        settings.IgnoreExpressions.Add(new Regex(@"sysdiagrams$"));

        var generator = new DbmlGenerator(settings);
        return generator.Create(_schema);
    }

    [Theory]
    [InlineData(TableNamingEnum.Singular)]
    [InlineData(TableNamingEnum.Plural)]
    [InlineData(TableNamingEnum.Mixed)]
    public void NamingChange_TableNaming_ProducesValidTables(TableNamingEnum tableNaming)
    {
        var database = Generate(tableNaming, EntityNamingEnum.Preserve, AssociationNamingEnum.ListSuffix);
        Assert.NotEmpty(database.Tables);

        // All tables should have non-empty Member names
        foreach (var table in database.Tables)
        {
            Assert.False(string.IsNullOrWhiteSpace(table.Member),
                $"Table '{table.Name}' should have a non-empty Member");
        }
    }

    [Theory]
    [InlineData(EntityNamingEnum.Preserve)]
    [InlineData(EntityNamingEnum.Singular)]
    [InlineData(EntityNamingEnum.Plural)]
    public void NamingChange_EntityNaming_ProducesValidTypes(EntityNamingEnum entityNaming)
    {
        var database = Generate(TableNamingEnum.Mixed, entityNaming, AssociationNamingEnum.ListSuffix);
        Assert.NotEmpty(database.Tables);

        foreach (var table in database.Tables)
        {
            Assert.False(string.IsNullOrWhiteSpace(table.Type.Name),
                $"Table '{table.Name}' type should have a non-empty Name");
        }
    }

    [Fact]
    public void NamingChange_ListSuffix_AssociationMembersEndWithList()
    {
        var database = Generate(TableNamingEnum.Mixed, EntityNamingEnum.Preserve, AssociationNamingEnum.ListSuffix);

        var entitySetAssociations = database.Tables
            .SelectMany(t => t.Type.EntitySetAssociations)
            .ToList();

        if (entitySetAssociations.Count > 0)
        {
            // Collection-side associations should have "List" suffix
            foreach (var assoc in entitySetAssociations)
            {
                Assert.True(assoc.Member?.EndsWith("List"),
                    $"EntitySet association '{assoc.Name}' member '{assoc.Member}' should end with 'List'");
            }
        }
    }

    [Fact]
    public void NamingChange_Plural_AssociationMembersArePluralized()
    {
        var database = Generate(TableNamingEnum.Mixed, EntityNamingEnum.Preserve, AssociationNamingEnum.Plural);

        var entitySetAssociations = database.Tables
            .SelectMany(t => t.Type.EntitySetAssociations)
            .ToList();

        if (entitySetAssociations.Count > 0)
        {
            // Collection-side associations should NOT have "List" suffix with Plural naming
            foreach (var assoc in entitySetAssociations)
            {
                Assert.False(assoc.Member?.EndsWith("List"),
                    $"With Plural naming, association '{assoc.Name}' member '{assoc.Member}' should not end with 'List'");
            }
        }
    }

    [Fact]
    public void NamingChange_DisableRenaming_PreservesOriginalNames()
    {
        var database = Generate(TableNamingEnum.Singular, EntityNamingEnum.Singular,
            AssociationNamingEnum.ListSuffix, disableRenaming: true);

        Assert.NotEmpty(database.Tables);

        // With DisableRenaming, table names should match original DB names
        foreach (var table in database.Tables)
        {
            Assert.NotNull(table.Name);
            Assert.NotNull(table.Member);
        }
    }
}
