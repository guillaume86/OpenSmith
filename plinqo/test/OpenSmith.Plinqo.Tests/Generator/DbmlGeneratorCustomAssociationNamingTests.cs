using System.Text.RegularExpressions;
using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.Generator;

public class DbmlGeneratorCustomAssociationNamingTests : IDisposable
{
    private readonly string _tempDir;

    public DbmlGeneratorCustomAssociationNamingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DbmlGenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private Database Generate(
        Action<GeneratorSettings>? configureSettings = null)
    {
        var schema = BuildSchema("FK_Order_OrderCustomer", "Order", "Customer");

        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, "test.dbml"),
            EntityNaming = EntityNamingEnum.Preserve,
            TableNaming = TableNamingEnum.Mixed,
            AssociationNaming = AssociationNamingEnum.ListSuffix,
        };
        settings.IncludeExpressions.Add(new Regex(".*"));

        configureSettings?.Invoke(settings);

        var generator = new DbmlGenerator(settings);
        return generator.Create(schema);
    }

    private static DatabaseSchema BuildSchema(string fkName, string foreignTableName, string primaryTableName)
    {
        var db = new DatabaseSchema
        {
            Name = "TestDb",
            Provider = new DatabaseProvider { Name = "SqlSchemaProvider" },
        };

        var primaryTable = new TableSchema
        {
            Name = primaryTableName,
            FullName = $"dbo.{primaryTableName}",
            Database = db,
            HasPrimaryKey = true,
        };
        var primaryPkCol = new ColumnSchema
        {
            Name = $"{primaryTableName}Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Table = primaryTable,
            Database = db,
        };
        primaryTable.Columns.Add(primaryPkCol);
        primaryTable.PrimaryKey = new PrimaryKeySchema();
        primaryTable.PrimaryKey.MemberColumns.Add(new MemberColumnSchema
        {
            Name = $"{primaryTableName}Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Column = primaryPkCol,
            Database = db,
        });

        var foreignTable = new TableSchema
        {
            Name = foreignTableName,
            FullName = $"dbo.{foreignTableName}",
            Database = db,
            HasPrimaryKey = true,
        };
        var foreignPkCol = new ColumnSchema
        {
            Name = $"{foreignTableName}Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Table = foreignTable,
            Database = db,
        };
        var foreignFkCol = new ColumnSchema
        {
            Name = $"{primaryTableName}Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsForeignKeyMember = true,
            AllowDBNull = false,
            Table = foreignTable,
            Database = db,
        };
        foreignTable.Columns.Add(foreignPkCol);
        foreignTable.Columns.Add(foreignFkCol);
        foreignTable.PrimaryKey = new PrimaryKeySchema();
        foreignTable.PrimaryKey.MemberColumns.Add(new MemberColumnSchema
        {
            Name = $"{foreignTableName}Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Column = foreignPkCol,
            Database = db,
        });

        var fk = new TableKeySchema
        {
            Name = fkName,
            ForeignKeyTable = foreignTable,
            PrimaryKeyTable = primaryTable,
        };
        fk.ForeignKeyMemberColumns.Add(new MemberColumnSchema
        {
            Name = $"{primaryTableName}Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsForeignKeyMember = true,
            AllowDBNull = false,
            Column = foreignFkCol,
            Database = db,
        });
        fk.PrimaryKeyMemberColumns.Add(new MemberColumnSchema
        {
            Name = $"{primaryTableName}Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Column = primaryPkCol,
            Database = db,
        });

        foreignTable.ForeignKeys.Add(fk);
        primaryTable.PrimaryKeys.Add(fk);

        db.Tables.Add(primaryTable);
        db.Tables.Add(foreignTable);

        return db;
    }

    [Fact]
    public void DefaultBehavior_NoDelegate_UsesStandardNaming()
    {
        var database = Generate();

        var orderTable = database.Tables.First(t => t.Member == "Order");
        var fkAssoc = orderTable.Type.Associations.First(a => a.IsForeignKey == true);

        // Standard behavior: forward association member = primary class name
        Assert.Equal("Customer", fkAssoc.Member);
    }

    [Fact]
    public void ResolveAssociationMemberName_WhenSet_OverridesForwardAssociation()
    {
        var database = Generate(settings =>
        {
            settings.ResolveAssociationMemberName = (fkName, foreignClass, primaryClass, prefix) =>
            {
                var parts = fkName.Split('_');
                if (parts.Length == 3 && parts[0] == "FK"
                    && parts[1].Equals(foreignClass, StringComparison.OrdinalIgnoreCase)
                    && parts[2].EndsWith(primaryClass))
                    return parts[2]; // "OrderCustomer"
                return null;
            };
        });

        var orderTable = database.Tables.First(t => t.Member == "Order");
        var fkAssoc = orderTable.Type.Associations.First(a => a.IsForeignKey == true);

        Assert.Equal("OrderCustomer", fkAssoc.Member);
    }

    [Fact]
    public void ResolveReverseAssociationMemberName_WhenSet_OverridesReverseAssociation()
    {
        var database = Generate(settings =>
        {
            settings.ResolveReverseAssociationMemberName = (fkName, foreignClass, primaryClass, prefix) =>
            {
                var parts = fkName.Split('_');
                if (parts.Length == 3 && parts[0] == "FK"
                    && parts[1].Equals(foreignClass, StringComparison.OrdinalIgnoreCase)
                    && parts[2].EndsWith(primaryClass))
                {
                    // "OrderCustomer" minus "Customer" = "Order", + "Order" = "OrderOrder"
                    return parts[2].Substring(0, parts[2].Length - primaryClass.Length) + parts[1];
                }
                return null;
            };
        });

        var customerTable = database.Tables.First(t => t.Member == "Customer");
        var reverseAssoc = customerTable.Type.Associations.First(a => a.IsForeignKey != true);

        // Hrweb reverse: "Order" prefix + "Order" table = "OrderOrder"
        // But ListSuffix is NOT applied when custom name is returned
        Assert.Equal("OrderOrder", reverseAssoc.Member);
    }

    [Fact]
    public void ResolveAssociationMemberName_ReturnsNull_FallsBackToDefault()
    {
        var database = Generate(settings =>
        {
            settings.ResolveAssociationMemberName = (fkName, foreignClass, primaryClass, prefix) => null;
        });

        var orderTable = database.Tables.First(t => t.Member == "Order");
        var fkAssoc = orderTable.Type.Associations.First(a => a.IsForeignKey == true);

        // Should fall back to standard naming
        Assert.Equal("Customer", fkAssoc.Member);
    }

    [Fact]
    public void ResolveReverseAssociationMemberName_ReturnsNull_FallsBackToDefaultWithListSuffix()
    {
        var database = Generate(settings =>
        {
            settings.ResolveReverseAssociationMemberName = (fkName, foreignClass, primaryClass, prefix) => null;
        });

        var customerTable = database.Tables.First(t => t.Member == "Customer");
        var reverseAssoc = customerTable.Type.Associations.First(a => a.IsForeignKey != true);

        // Default with ListSuffix
        Assert.EndsWith("List", reverseAssoc.Member);
    }

    [Fact]
    public void BothDelegates_WorkTogether_HrwebStyle()
    {
        var database = Generate(settings =>
        {
            // Hrweb naming: FK_{ForeignTable}_{LinkName}
            settings.ResolveAssociationMemberName = (fkName, foreignClass, primaryClass, prefix) =>
            {
                var parts = fkName.Split('_');
                if (parts.Length == 3 && parts[0] == "FK"
                    && parts[1].Equals(foreignClass, StringComparison.OrdinalIgnoreCase)
                    && parts[2].EndsWith(primaryClass))
                    return parts[2];
                return null;
            };
            settings.ResolveReverseAssociationMemberName = (fkName, foreignClass, primaryClass, prefix) =>
            {
                var parts = fkName.Split('_');
                if (parts.Length == 3 && parts[0] == "FK"
                    && parts[1].Equals(foreignClass, StringComparison.OrdinalIgnoreCase)
                    && parts[2].EndsWith(primaryClass))
                    return parts[2].Substring(0, parts[2].Length - primaryClass.Length) + parts[1];
                return null;
            };
        });

        var orderTable = database.Tables.First(t => t.Member == "Order");
        var fkAssoc = orderTable.Type.Associations.First(a => a.IsForeignKey == true);
        Assert.Equal("OrderCustomer", fkAssoc.Member);

        var customerTable = database.Tables.First(t => t.Member == "Customer");
        var reverseAssoc = customerTable.Type.Associations.First(a => a.IsForeignKey != true);
        Assert.Equal("OrderOrder", reverseAssoc.Member);
    }
}
