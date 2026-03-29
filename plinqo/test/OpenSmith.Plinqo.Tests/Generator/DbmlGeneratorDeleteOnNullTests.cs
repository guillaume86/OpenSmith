using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.Generator;

public class DbmlGeneratorDeleteOnNullTests : IDisposable
{
    private readonly string _tempDir;

    public DbmlGeneratorDeleteOnNullTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DbmlGenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private Database GenerateFromSchema(DatabaseSchema schema, bool includeDeleteOnNull = true)
    {
        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, "test.dbml"),
            IncludeDeleteOnNull = includeDeleteOnNull,
            EntityNaming = EntityNamingEnum.Preserve,
            TableNaming = TableNamingEnum.Mixed,
            AssociationNaming = AssociationNamingEnum.ListSuffix,
        };
        settings.IncludeExpressions.Add(new System.Text.RegularExpressions.Regex(".*"));

        var generator = new DbmlGenerator(settings);
        return generator.Create(schema);
    }

    private static DatabaseSchema BuildTwoTableSchema(bool fkColumnNullable)
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
            AllowDBNull = fkColumnNullable,
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
            AllowDBNull = fkColumnNullable,
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
    public void NonNullableFkColumn_FkSide_DeleteOnNull_True()
    {
        // Original CodeSmith only sets DeleteOnNull=true on FK side when FK columns are non-nullable
        var schema = BuildTwoTableSchema(fkColumnNullable: false);
        var database = GenerateFromSchema(schema);

        var childTable = database.Tables.First(t => t.Member == "Child");

        var fkAssociation = childTable.Type.Associations
            .First(a => a.Name == "FK_Child_Parent" && a.IsForeignKey == true);
        Assert.Equal(true, fkAssociation.DeleteOnNull);
    }

    [Fact]
    public void NonNullableFkColumn_PrimarySide_DeleteOnNull_NotSet()
    {
        // Original CodeSmith does NOT set DeleteOnNull on the primary (non-FK) side
        var schema = BuildTwoTableSchema(fkColumnNullable: false);
        var database = GenerateFromSchema(schema);

        var parentTable = database.Tables.First(t => t.Member == "Parent");

        var pkAssociation = parentTable.Type.Associations
            .First(a => a.Name == "FK_Child_Parent" && a.IsForeignKey != true);
        Assert.Null(pkAssociation.DeleteOnNull);
    }

    [Fact]
    public void NullableFkColumn_FkSide_DeleteOnNull_NotSet()
    {
        // When FK column is nullable, IsTableDeleteOnNull returns false,
        // and original CodeSmith only sets DeleteOnNull when it would be true
        var schema = BuildTwoTableSchema(fkColumnNullable: true);
        var database = GenerateFromSchema(schema);

        var childTable = database.Tables.First(t => t.Member == "Child");

        var fkAssociation = childTable.Type.Associations
            .First(a => a.Name == "FK_Child_Parent" && a.IsForeignKey == true);
        Assert.Null(fkAssociation.DeleteOnNull);
    }

    [Fact]
    public void NullableFkColumn_PrimarySide_DeleteOnNull_NotSet()
    {
        var schema = BuildTwoTableSchema(fkColumnNullable: true);
        var database = GenerateFromSchema(schema);

        var parentTable = database.Tables.First(t => t.Member == "Parent");

        var pkAssociation = parentTable.Type.Associations
            .First(a => a.Name == "FK_Child_Parent" && a.IsForeignKey != true);
        Assert.Null(pkAssociation.DeleteOnNull);
    }

    [Fact]
    public void IncludeDeleteOnNull_False_DeleteOnNull_Null()
    {
        var schema = BuildTwoTableSchema(fkColumnNullable: false);
        var database = GenerateFromSchema(schema, includeDeleteOnNull: false);

        var childTable = database.Tables.First(t => t.Member == "Child");

        var fkAssociation = childTable.Type.Associations
            .First(a => a.Name == "FK_Child_Parent" && a.IsForeignKey == true);
        Assert.Null(fkAssociation.DeleteOnNull);
    }
}
