using System.Text.RegularExpressions;
using LinqToSqlShared.DbmlObjectModel;
using LinqToSqlShared.Generator;
using SchemaExplorer;

namespace OpenSmith.Tests.Generator;

public class DbmlGeneratorIgnoreColumnTests : IDisposable
{
    private readonly string _tempDir;

    public DbmlGeneratorIgnoreColumnTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DbmlGenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private Database GenerateWithIgnore(DatabaseSchema schema, string ignorePattern)
    {
        var settings = new GeneratorSettings
        {
            MappingFile = Path.Combine(_tempDir, "test.dbml"),
            EntityNaming = EntityNamingEnum.Preserve,
            TableNaming = TableNamingEnum.Mixed,
            AssociationNaming = AssociationNamingEnum.ListSuffix,
        };
        settings.IncludeExpressions.Add(new Regex(".*"));
        settings.IgnoreExpressions.Add(new Regex(ignorePattern));

        var generator = new DbmlGenerator(settings);
        return generator.Create(schema);
    }

    private static DatabaseSchema BuildSchemaWithColumns(params string[] columnNames)
    {
        var db = new DatabaseSchema
        {
            Name = "TestDb",
            Provider = new DatabaseProvider { Name = "SqlSchemaProvider" },
        };

        var table = new TableSchema
        {
            Name = "MyTable",
            FullName = "dbo.MyTable",
            Database = db,
            HasPrimaryKey = true,
        };

        var pkCol = new ColumnSchema
        {
            Name = "Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Table = table,
            Database = db,
        };
        table.Columns.Add(pkCol);
        table.PrimaryKey = new PrimaryKeySchema();
        table.PrimaryKey.MemberColumns.Add(new MemberColumnSchema
        {
            Name = "Id",
            SystemType = typeof(int),
            NativeType = "int",
            IsPrimaryKeyMember = true,
            AllowDBNull = false,
            Column = pkCol,
            Database = db,
        });

        foreach (var name in columnNames)
        {
            table.Columns.Add(new ColumnSchema
            {
                Name = name,
                SystemType = typeof(string),
                NativeType = "varchar",
                AllowDBNull = true,
                Table = table,
                Database = db,
            });
        }

        db.Tables.Add(table);
        return db;
    }

    [Fact]
    public void Columns_MatchingIgnorePattern_AreExcluded()
    {
        var schema = BuildSchemaWithColumns("NormalCol", "Cm1900250101", "AnotherCol");
        var database = GenerateWithIgnore(schema, @"^Cm\d{10}$");

        var table = database.Tables["dbo.MyTable"];
        Assert.NotNull(table);

        // Cm1900250101 matches ^Cm\d{10}$ and should be excluded
        Assert.False(table.Type.Columns.Contains("Cm1900250101"),
            "Column matching ignore pattern should be excluded");
    }

    [Fact]
    public void Columns_NotMatchingIgnorePattern_AreKept()
    {
        var schema = BuildSchemaWithColumns("NormalCol", "Cm1900250101", "AnotherCol");
        var database = GenerateWithIgnore(schema, @"^Cm\d{10}$");

        var table = database.Tables["dbo.MyTable"];
        Assert.True(table.Type.Columns.Contains("NormalCol"));
        Assert.True(table.Type.Columns.Contains("AnotherCol"));
    }

    [Fact]
    public void Columns_SimilarButNotMatchingPattern_AreKept()
    {
        // Cm19007Code is only 11 chars (Cm + 5 digits + Code) - should NOT match ^Cm\d{10}$
        var schema = BuildSchemaWithColumns("Cm19007Code", "Cm1900250101");
        var database = GenerateWithIgnore(schema, @"^Cm\d{10}$");

        var table = database.Tables["dbo.MyTable"];
        Assert.True(table.Type.Columns.Contains("Cm19007Code"),
            "Column not matching ignore pattern should be kept");
        Assert.False(table.Type.Columns.Contains("Cm1900250101"),
            "Column matching ignore pattern should be excluded");
    }
}
