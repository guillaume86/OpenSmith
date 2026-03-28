using SchemaExplorer;

namespace OpenSmith.Tests.SchemaExplorer;

public class TableSchemaTests
{
    [Fact]
    public void ForeignKeyColumns_ReturnsOnlyForeignKeyMembers()
    {
        var table = new TableSchema();
        table.Columns.Add(new ColumnSchema { Name = "Id", IsForeignKeyMember = false });
        table.Columns.Add(new ColumnSchema { Name = "ParentId", IsForeignKeyMember = true });
        table.Columns.Add(new ColumnSchema { Name = "Name", IsForeignKeyMember = false });
        table.Columns.Add(new ColumnSchema { Name = "CategoryId", IsForeignKeyMember = true });

        var fkColumns = table.ForeignKeyColumns.ToList();

        Assert.Equal(2, fkColumns.Count);
        Assert.Equal("ParentId", fkColumns[0].Name);
        Assert.Equal("CategoryId", fkColumns[1].Name);
    }

    [Fact]
    public void Indexes_Collection_IsAccessible()
    {
        var table = new TableSchema();
        var index = new IndexSchema { Name = "IX_Test", IsUnique = true };
        index.MemberColumns.Add(new MemberColumnSchema());

        table.Indexes.Add(index);

        Assert.Single(table.Indexes);
        Assert.True(table.Indexes[0].IsUnique);
    }
}
