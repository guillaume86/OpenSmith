using OpenSmith.Plinqo.Tests.Fixtures;
using SchemaExplorer;

namespace OpenSmith.Plinqo.Tests.Schema;

[Collection("AdventureWorks")]
public class AdventureWorksSchemaTests
{
    private readonly DatabaseSchema _schema;

    public AdventureWorksSchemaTests(AdventureWorksFixture fixture)
    {
        _schema = fixture.Schema;
    }

    [Fact]
    public void Schema_HasExpectedTableCount()
    {
        // AdventureWorks2022 has ~71 user tables
        Assert.InRange(_schema.Tables.Count, 65, 80);
    }

    [Theory]
    [InlineData("Person.Person")]
    [InlineData("Sales.SalesOrderHeader")]
    [InlineData("Sales.SalesOrderDetail")]
    [InlineData("Production.Product")]
    [InlineData("HumanResources.Employee")]
    [InlineData("Purchasing.PurchaseOrderHeader")]
    [InlineData("Production.ProductCategory")]
    [InlineData("Production.ProductSubcategory")]
    public void Schema_ContainsKnownTables(string tableName)
    {
        var table = _schema.Tables[tableName];
        Assert.NotNull(table);
    }

    [Fact]
    public void Schema_HasExpectedViewCount()
    {
        // AdventureWorks2022 has ~20 views
        Assert.InRange(_schema.Views.Count, 15, 25);
    }

    [Theory]
    [InlineData("Sales.vSalesPerson")]
    [InlineData("HumanResources.vEmployee")]
    [InlineData("Production.vProductAndDescription")]
    public void Schema_ContainsKnownViews(string viewName)
    {
        var view = _schema.Views.FirstOrDefault(v =>
            string.Equals(v.FullName, viewName, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(view);
    }

    [Fact]
    public void Schema_MultipleSchemas_AllPresent()
    {
        var schemas = _schema.Tables.Select(t => t.FullName.Split('.')[0]).Distinct().ToHashSet();
        Assert.Contains("Person", schemas);
        Assert.Contains("Sales", schemas);
        Assert.Contains("Production", schemas);
        Assert.Contains("HumanResources", schemas);
        Assert.Contains("Purchasing", schemas);
    }

    [Fact]
    public void Schema_ForeignKeys_AreDetected()
    {
        var salesOrderDetail = _schema.Tables["Sales.SalesOrderDetail"];
        Assert.NotNull(salesOrderDetail);
        Assert.NotEmpty(salesOrderDetail.ForeignKeys);

        var fkToHeader = salesOrderDetail.ForeignKeys
            .FirstOrDefault(fk => fk.PrimaryKeyTable.Name == "SalesOrderHeader");
        Assert.NotNull(fkToHeader);
    }

    [Fact]
    public void Schema_CompositePrimaryKeys_AreDetected()
    {
        var salesOrderDetail = _schema.Tables["Sales.SalesOrderDetail"];
        Assert.NotNull(salesOrderDetail);
        Assert.True(salesOrderDetail.HasPrimaryKey);
        Assert.True(salesOrderDetail.PrimaryKey.MemberColumns.Count >= 2);
    }

    [Theory]
    [InlineData("Production.Product", "rowguid", typeof(Guid))]
    [InlineData("Sales.SalesOrderHeader", "TotalDue", typeof(decimal))]
    [InlineData("Person.Person", "ModifiedDate", typeof(DateTime))]
    public void Schema_DataTypes_MapCorrectly(string tableName, string columnName, Type expectedType)
    {
        var table = _schema.Tables[tableName];
        Assert.NotNull(table);

        var column = table.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(column);
        Assert.Equal(expectedType, column.SystemType);
    }

    [Fact]
    public void Schema_HasStoredProceduresOrFunctions()
    {
        Assert.NotEmpty(_schema.Commands);
    }

    [Fact]
    public void Schema_TablesHaveColumns()
    {
        foreach (var table in _schema.Tables)
        {
            Assert.NotEmpty(table.Columns);
        }
    }
}
