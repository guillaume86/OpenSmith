using System.Text.RegularExpressions;
using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.Generator;

public class DbmlGeneratorExcludeReverseAssociationTests : IDisposable
{
    private readonly string _tempDir;

    public DbmlGeneratorExcludeReverseAssociationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DbmlGenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private Database GenerateWithExclude(DatabaseSchema schema, params string[] excludePatterns)
    {
        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, "test.dbml"),
            EntityNaming = EntityNamingEnum.Preserve,
            TableNaming = TableNamingEnum.Mixed,
            AssociationNaming = AssociationNamingEnum.ListSuffix,
        };
        settings.IncludeExpressions.Add(new Regex(".*"));

        foreach (var pattern in excludePatterns)
            settings.ExcludeReverseAssociationsExpressions.Add(new Regex(pattern));

        var generator = new DbmlGenerator(settings);
        return generator.Create(schema);
    }

    private static DatabaseSchema BuildTwoTableSchema()
    {
        var db = new DatabaseSchema
        {
            Name = "TestDb",
            Provider = new DatabaseProvider { Name = "SqlSchemaProvider" },
        };

        var parentTable = new TableSchema
        {
            Name = "Parent",
            FullName = "dbo.Parent",
            Database = db,
            HasPrimaryKey = true,
        };
        var parentPkCol = new ColumnSchema
        {
            Name = "ParentId",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Table = parentTable,
            Database = db,
        };
        parentTable.Columns.Add(parentPkCol);
        parentTable.PrimaryKey = new PrimaryKeySchema();
        parentTable.PrimaryKey.MemberColumns.Add(new MemberColumnSchema
        {
            Name = "ParentId",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Column = parentPkCol,
            Database = db,
        });

        var childTable = new TableSchema
        {
            Name = "Child",
            FullName = "dbo.Child",
            Database = db,
            HasPrimaryKey = true,
        };
        var childPkCol = new ColumnSchema
        {
            Name = "ChildId",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Table = childTable,
            Database = db,
        };
        var childFkCol = new ColumnSchema
        {
            Name = "ParentId",
            SystemType = typeof(int),
            NativeType = "int",
            IsForeignKeyMember = true,
            AllowDBNull = false,
            Table = childTable,
            Database = db,
        };
        childTable.Columns.Add(childPkCol);
        childTable.Columns.Add(childFkCol);
        childTable.PrimaryKey = new PrimaryKeySchema();
        childTable.PrimaryKey.MemberColumns.Add(new MemberColumnSchema
        {
            Name = "ChildId",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Column = childPkCol,
            Database = db,
        });

        var fk = new TableKeySchema
        {
            Name = "FK_Child_Parent",
            ForeignKeyTable = childTable,
            PrimaryKeyTable = parentTable,
        };
        fk.ForeignKeyMemberColumns.Add(new MemberColumnSchema
        {
            Name = "ParentId",
            SystemType = typeof(int),
            NativeType = "int",
            IsForeignKeyMember = true,
            AllowDBNull = false,
            Column = childFkCol,
            Database = db,
        });
        fk.PrimaryKeyMemberColumns.Add(new MemberColumnSchema
        {
            Name = "ParentId",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Column = parentPkCol,
            Database = db,
        });

        childTable.ForeignKeys.Add(fk);
        parentTable.PrimaryKeys.Add(fk);

        db.Tables.Add(parentTable);
        db.Tables.Add(childTable);

        return db;
    }

    [Fact]
    public void ReverseAssociation_MatchingExcludePattern_IsExcluded()
    {
        var schema = BuildTwoTableSchema();
        var database = GenerateWithExclude(schema, "FK_Child_Parent");

        var parentTable = database.Tables.First(t => t.Member == "Parent");
        Assert.Empty(parentTable.Type.Associations);
    }

    [Fact]
    public void ForeignKeyAssociation_StillExists_WhenReverseIsExcluded()
    {
        var schema = BuildTwoTableSchema();
        var database = GenerateWithExclude(schema, "FK_Child_Parent");

        var childTable = database.Tables.First(t => t.Member == "Child");
        var fkAssociation = childTable.Type.Associations
            .FirstOrDefault(a => a.Name == "FK_Child_Parent" && a.IsForeignKey == true);
        Assert.NotNull(fkAssociation);
    }

    [Fact]
    public void ReverseAssociation_NotMatchingPattern_IsKept()
    {
        var schema = BuildTwoTableSchema();
        var database = GenerateWithExclude(schema, "FK_SomethingElse");

        var parentTable = database.Tables.First(t => t.Member == "Parent");
        var reverseAssociation = parentTable.Type.Associations
            .FirstOrDefault(a => a.Name == "FK_Child_Parent" && a.IsForeignKey != true);
        Assert.NotNull(reverseAssociation);
    }

    [Fact]
    public void NoExcludePatterns_AllReverseAssociationsKept()
    {
        var schema = BuildTwoTableSchema();
        var database = GenerateWithExclude(schema);

        var parentTable = database.Tables.First(t => t.Member == "Parent");
        var reverseAssociation = parentTable.Type.Associations
            .FirstOrDefault(a => a.Name == "FK_Child_Parent" && a.IsForeignKey != true);
        Assert.NotNull(reverseAssociation);
    }
}
