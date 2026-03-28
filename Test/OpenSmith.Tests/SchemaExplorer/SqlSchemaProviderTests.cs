using OpenSmith.Tests.Fixtures;
using SchemaExplorer;

namespace OpenSmith.Tests.SchemaExplorer;

[Collection("SqlServer")]
public class SqlSchemaProviderTests
{
    private readonly DatabaseSchema _schema;

    public SqlSchemaProviderTests(SqlServerFixture fixture)
    {
        var provider = new SqlSchemaProvider();
        _schema = provider.GetDatabaseSchema(fixture.ConnectionString);
    }

    // Phase 0: Infrastructure smoke test
    [Fact]
    public void GetDatabaseSchema_ReturnsNonNull()
    {
        Assert.NotNull(_schema);
    }

    // Phase 1: Database basics
    [Fact]
    public void Database_HasCorrectName()
    {
        Assert.NotEmpty(_schema.Name);
    }

    [Fact]
    public void Database_HasConnectionString()
    {
        Assert.NotEmpty(_schema.ConnectionString);
    }

    [Fact]
    public void Database_ProviderName_IsSqlSchemaProvider()
    {
        Assert.Equal("SqlSchemaProvider", _schema.Provider.Name);
    }

    // Phase 2: Tables
    [Fact]
    public void Tables_ReturnsAllUserTables()
    {
        Assert.Equal(5, _schema.Tables.Count);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Order")]
    [InlineData("OrderItem")]
    [InlineData("OrderItemNote")]
    [InlineData("AuditLog")]
    public void Tables_ContainsExpectedTable(string tableName)
    {
        Assert.Contains(_schema.Tables, t => t.Name == tableName);
    }

    [Theory]
    [InlineData("Customer", "dbo.Customer")]
    [InlineData("Order", "Sales.Order")]
    [InlineData("OrderItem", "dbo.OrderItem")]
    public void Tables_HaveCorrectFullName(string name, string expectedFullName)
    {
        var table = _schema.Tables.First(t => t.Name == name);
        Assert.Equal(expectedFullName, table.FullName);
    }

    [Fact]
    public void Tables_HaveDatabaseReference()
    {
        Assert.All(_schema.Tables, t => Assert.Same(_schema, t.Database));
    }

    // Phase 3: Columns
    [Fact]
    public void Customer_HasExpectedColumnCount()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        Assert.Equal(9, table.Columns.Count);
    }

    [Theory]
    [InlineData("CustomerId", "int")]
    [InlineData("FirstName", "nvarchar")]
    [InlineData("Balance", "decimal")]
    [InlineData("IsActive", "bit")]
    [InlineData("RowGuid", "uniqueidentifier")]
    [InlineData("Photo", "varbinary")]
    [InlineData("BirthDate", "date")]
    public void Customer_Columns_HaveCorrectNativeType(string columnName, string expectedType)
    {
        var column = GetCustomerColumn(columnName);
        Assert.Equal(expectedType, column.NativeType);
    }

    [Theory]
    [InlineData("CustomerId", typeof(int))]
    [InlineData("FirstName", typeof(string))]
    [InlineData("Balance", typeof(decimal))]
    [InlineData("IsActive", typeof(bool))]
    [InlineData("RowGuid", typeof(Guid))]
    [InlineData("Photo", typeof(byte[]))]
    [InlineData("BirthDate", typeof(DateTime))]
    public void Customer_Columns_HaveCorrectSystemType(string columnName, Type expectedType)
    {
        var column = GetCustomerColumn(columnName);
        Assert.Equal(expectedType, column.SystemType);
    }

    [Fact]
    public void Customer_Balance_HasCorrectPrecisionAndScale()
    {
        var column = GetCustomerColumn("Balance");
        Assert.Equal(18, column.Precision);
        Assert.Equal(2, column.Scale);
    }

    [Fact]
    public void Customer_FirstName_HasCorrectSize()
    {
        var column = GetCustomerColumn("FirstName");
        Assert.Equal(200, column.Size); // nvarchar(100) = 200 bytes
    }

    [Theory]
    [InlineData("Email", true)]
    [InlineData("FirstName", false)]
    [InlineData("CustomerId", false)]
    public void Customer_Columns_HaveCorrectAllowDBNull(string columnName, bool expectedNullable)
    {
        var column = GetCustomerColumn(columnName);
        Assert.Equal(expectedNullable, column.AllowDBNull);
    }

    [Fact]
    public void Columns_HaveTableReference()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        Assert.All(table.Columns, c => Assert.Same(table, c.Table));
    }

    [Fact]
    public void Columns_HaveCorrectFullName()
    {
        var column = GetCustomerColumn("CustomerId");
        Assert.Equal("dbo.Customer.CustomerId", column.FullName);
    }

    [Fact]
    public void Columns_HaveDatabaseReference()
    {
        var column = GetCustomerColumn("CustomerId");
        Assert.Same(_schema, column.Database);
    }

    // Phase 3 continued: Order table column types
    [Fact]
    public void Order_Total_HasMoneyType()
    {
        var table = _schema.Tables.First(t => t.Name == "Order");
        var column = table.Columns.First(c => c.Name == "Total");
        Assert.Equal("money", column.NativeType);
        Assert.Equal(typeof(decimal), column.SystemType);
    }

    [Fact]
    public void Order_OrderDate_HasDatetime2Type()
    {
        var table = _schema.Tables.First(t => t.Name == "Order");
        var column = table.Columns.First(c => c.Name == "OrderDate");
        Assert.Equal("datetime2", column.NativeType);
        Assert.Equal(typeof(DateTime), column.SystemType);
    }

    [Fact]
    public void AuditLog_CreatedAt_HasDatetimeoffsetType()
    {
        var table = _schema.Tables.First(t => t.Name == "AuditLog");
        var column = table.Columns.First(c => c.Name == "CreatedAt");
        Assert.Equal("datetimeoffset", column.NativeType);
        Assert.Equal(typeof(DateTimeOffset), column.SystemType);
    }

    [Fact]
    public void AuditLog_LogId_HasBigintType()
    {
        var table = _schema.Tables.First(t => t.Name == "AuditLog");
        var column = table.Columns.First(c => c.Name == "LogId");
        Assert.Equal("bigint", column.NativeType);
        Assert.Equal(typeof(long), column.SystemType);
    }

    [Fact]
    public void OrderItem_Quantity_HasSmallintType()
    {
        var table = _schema.Tables.First(t => t.Name == "OrderItem");
        var column = table.Columns.First(c => c.Name == "Quantity");
        Assert.Equal("smallint", column.NativeType);
        Assert.Equal(typeof(short), column.SystemType);
    }

    // Phase 4: Primary keys
    [Fact]
    public void Customer_HasPrimaryKey()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        Assert.True(table.HasPrimaryKey);
        Assert.NotNull(table.PrimaryKey);
    }

    [Fact]
    public void Customer_PrimaryKey_HasSingleColumn()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        Assert.Single(table.PrimaryKey.MemberColumns);
        Assert.Equal("CustomerId", table.PrimaryKey.MemberColumns[0].Name);
    }

    [Fact]
    public void OrderItem_HasCompositePrimaryKey()
    {
        var table = _schema.Tables.First(t => t.Name == "OrderItem");
        Assert.True(table.HasPrimaryKey);
        Assert.Equal(2, table.PrimaryKey.MemberColumns.Count);
    }

    [Fact]
    public void AuditLog_HasNoPrimaryKey()
    {
        var table = _schema.Tables.First(t => t.Name == "AuditLog");
        Assert.False(table.HasPrimaryKey);
        Assert.Null(table.PrimaryKey);
    }

    [Fact]
    public void Customer_CustomerId_IsPrimaryKeyMember()
    {
        var column = GetCustomerColumn("CustomerId");
        Assert.True(column.IsPrimaryKeyMember);
    }

    [Fact]
    public void Customer_FirstName_IsNotPrimaryKeyMember()
    {
        var column = GetCustomerColumn("FirstName");
        Assert.False(column.IsPrimaryKeyMember);
    }

    [Fact]
    public void PrimaryKey_MemberColumns_HaveColumnReference()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        var memberCol = table.PrimaryKey.MemberColumns[0];
        Assert.NotNull(memberCol.Column);
        Assert.Equal("CustomerId", memberCol.Column.Name);
        Assert.Same(table.Columns.First(c => c.Name == "CustomerId"), memberCol.Column);
    }

    [Fact]
    public void PrimaryKey_MemberColumns_HaveCorrectTypeInfo()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        var memberCol = table.PrimaryKey.MemberColumns[0];
        Assert.Equal("int", memberCol.NativeType);
        Assert.Equal(typeof(int), memberCol.SystemType);
        Assert.True(memberCol.IsPrimaryKeyMember);
    }

    // Phase 5: Unique constraints
    [Fact]
    public void Customer_Email_IsUnique()
    {
        var column = GetCustomerColumn("Email");
        Assert.True(column.IsUnique);
    }

    [Fact]
    public void Customer_FirstName_IsNotUnique()
    {
        var column = GetCustomerColumn("FirstName");
        Assert.False(column.IsUnique);
    }

    // Phase 6: Foreign keys
    [Fact]
    public void Order_HasForeignKey()
    {
        var table = _schema.Tables.First(t => t.Name == "Order");
        Assert.Single(table.ForeignKeys);
    }

    [Fact]
    public void Order_ForeignKey_HasCorrectName()
    {
        var table = _schema.Tables.First(t => t.Name == "Order");
        Assert.Equal("FK_Order_Customer", table.ForeignKeys[0].Name);
    }

    [Fact]
    public void Order_ForeignKey_TablesAreCorrect()
    {
        var fk = _schema.Tables.First(t => t.Name == "Order").ForeignKeys[0];
        Assert.Equal("Order", fk.ForeignKeyTable.Name);
        Assert.Equal("Customer", fk.PrimaryKeyTable.Name);
    }

    [Fact]
    public void Order_ForeignKey_MemberColumnsAreCorrect()
    {
        var fk = _schema.Tables.First(t => t.Name == "Order").ForeignKeys[0];
        Assert.Single(fk.ForeignKeyMemberColumns);
        Assert.Equal("CustomerId", fk.ForeignKeyMemberColumns[0].Name);
        Assert.Single(fk.PrimaryKeyMemberColumns);
        Assert.Equal("CustomerId", fk.PrimaryKeyMemberColumns[0].Name);
    }

    [Fact]
    public void OrderItemNote_HasCompositeForeignKey()
    {
        var table = _schema.Tables.First(t => t.Name == "OrderItemNote");
        var fk = table.ForeignKeys.First(f => f.Name == "FK_OrderItemNote_OrderItem");
        Assert.Equal(2, fk.ForeignKeyMemberColumns.Count);
        Assert.Equal(2, fk.PrimaryKeyMemberColumns.Count);
    }

    [Fact]
    public void Order_CustomerId_IsForeignKeyMember()
    {
        var table = _schema.Tables.First(t => t.Name == "Order");
        var column = table.Columns.First(c => c.Name == "CustomerId");
        Assert.True(column.IsForeignKeyMember);
    }

    [Fact]
    public void Customer_HasPrimaryKeysCollection()
    {
        // Customer is referenced by FK_Order_Customer, so it should appear in PrimaryKeys
        var table = _schema.Tables.First(t => t.Name == "Customer");
        Assert.True(table.PrimaryKeys.Count >= 1);
    }

    [Fact]
    public void ForeignKey_HasDatabaseReference()
    {
        var fk = _schema.Tables.First(t => t.Name == "Order").ForeignKeys[0];
        Assert.Same(_schema, fk.Database);
    }

    [Fact]
    public void ForeignKey_MemberColumns_HaveColumnReference()
    {
        var fk = _schema.Tables.First(t => t.Name == "Order").ForeignKeys[0];
        var fkMember = fk.ForeignKeyMemberColumns[0];
        Assert.NotNull(fkMember.Column);
        Assert.Equal("CustomerId", fkMember.Column.Name);
    }

    [Fact]
    public void ForeignKey_MemberColumns_HaveForeignKeyMemberFlag()
    {
        var fk = _schema.Tables.First(t => t.Name == "Order").ForeignKeys[0];
        Assert.True(fk.ForeignKeyMemberColumns[0].IsForeignKeyMember);
    }

    // Phase 7: Extended properties
    [Fact]
    public void Customer_HasIdentityExtendedProperty()
    {
        var column = GetCustomerColumn("CustomerId");
        Assert.True(column.ExtendedProperties.Contains("CS_IsIdentity"));
        Assert.Equal("true", column.ExtendedProperties["CS_IsIdentity"].Value.ToString());
    }

    [Fact]
    public void Customer_FirstName_HasNoIdentityProperty()
    {
        var column = GetCustomerColumn("FirstName");
        // Non-identity columns should have CS_IsIdentity = false
        Assert.True(column.ExtendedProperties.Contains("CS_IsIdentity"));
        Assert.Equal("false", column.ExtendedProperties["CS_IsIdentity"].Value.ToString());
    }

    [Fact]
    public void Customer_HasComputedExtendedProperty()
    {
        var column = GetCustomerColumn("CustomerId");
        Assert.True(column.ExtendedProperties.Contains("CS_IsComputed"));
    }

    [Fact]
    public void Customer_Table_HasMSDescriptionExtendedProperty()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        Assert.True(table.ExtendedProperties.Contains("MS_Description"));
        Assert.Equal("Main customer table", table.ExtendedProperties["MS_Description"].Value.ToString());
    }

    [Fact]
    public void Customer_Email_HasMSDescriptionExtendedProperty()
    {
        var column = GetCustomerColumn("Email");
        Assert.True(column.ExtendedProperties.Contains("MS_Description"));
        Assert.Equal("Customer email address", column.ExtendedProperties["MS_Description"].Value.ToString());
    }

    [Fact]
    public void Order_FK_HasCascadeDeleteExtendedProperty()
    {
        var fk = _schema.Tables.First(t => t.Name == "Order").ForeignKeys[0];
        Assert.True(fk.ExtendedProperties.Contains("CS_CascadeDelete"));
        Assert.Equal(true, fk.ExtendedProperties["CS_CascadeDelete"].Value);
    }

    [Fact]
    public void OrderItemNote_FK_HasNoCascadeDelete()
    {
        var fk = _schema.Tables.First(t => t.Name == "OrderItemNote").ForeignKeys[0];
        Assert.True(fk.ExtendedProperties.Contains("CS_CascadeDelete"));
        Assert.Equal(false, fk.ExtendedProperties["CS_CascadeDelete"].Value);
    }

    // Phase 8: Views
    [Fact]
    public void Views_ReturnsAllViews()
    {
        Assert.Equal(2, _schema.Views.Count);
    }

    [Theory]
    [InlineData("ActiveCustomers", "dbo.ActiveCustomers")]
    [InlineData("OrderSummary", "Sales.OrderSummary")]
    public void Views_HaveCorrectNameAndFullName(string name, string expectedFullName)
    {
        var view = _schema.Views.First(v => v.Name == name);
        Assert.Equal(expectedFullName, view.FullName);
    }

    [Fact]
    public void ActiveCustomers_HasCorrectColumnCount()
    {
        var view = _schema.Views.First(v => v.Name == "ActiveCustomers");
        Assert.Equal(4, view.Columns.Count);
    }

    [Fact]
    public void ViewColumns_HaveCorrectTypes()
    {
        var view = _schema.Views.First(v => v.Name == "ActiveCustomers");
        var col = view.Columns.First(c => c.Name == "CustomerId");
        Assert.Equal("int", col.NativeType);
        Assert.Equal(typeof(int), col.SystemType);
    }

    [Fact]
    public void Views_HaveDatabaseReference()
    {
        Assert.All(_schema.Views, v => Assert.Same(_schema, v.Database));
    }

    [Fact]
    public void ViewColumns_HaveDatabaseReference()
    {
        var view = _schema.Views.First(v => v.Name == "ActiveCustomers");
        Assert.All(view.Columns, c => Assert.Same(_schema, c.Database));
    }

    [Fact]
    public void ViewColumns_HaveCorrectFullName()
    {
        var view = _schema.Views.First(v => v.Name == "ActiveCustomers");
        var col = view.Columns.First(c => c.Name == "CustomerId");
        Assert.Equal("dbo.ActiveCustomers.CustomerId", col.FullName);
    }

    // Phase 9: Commands (procedures & functions)
    [Fact]
    public void Commands_ReturnsAllCommands()
    {
        // 3 procedures + 1 scalar function = 4
        Assert.Equal(4, _schema.Commands.Count);
    }

    [Theory]
    [InlineData("GetCustomerOrders", "dbo.GetCustomerOrders")]
    [InlineData("GetCustomerCount", "dbo.GetCustomerCount")]
    [InlineData("PurgeOldOrders", "Sales.PurgeOldOrders")]
    [InlineData("GetCustomerBalance", "dbo.GetCustomerBalance")]
    public void Commands_HaveCorrectNameAndFullName(string name, string expectedFullName)
    {
        var cmd = _schema.Commands.First(c => c.Name == name);
        Assert.Equal(expectedFullName, cmd.FullName);
    }

    [Fact]
    public void Commands_HaveDatabaseReference()
    {
        Assert.All(_schema.Commands, c => Assert.Same(_schema, c.Database));
    }

    [Fact]
    public void GetCustomerOrders_HasInputParameters()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        var inputParams = cmd.Parameters.Where(p => p.Direction == System.Data.ParameterDirection.Input).ToList();
        Assert.Equal(2, inputParams.Count);
    }

    [Fact]
    public void GetCustomerOrders_Parameters_HaveCorrectTypes()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        var customerId = cmd.Parameters.First(p => p.Name == "@CustomerId");
        Assert.Equal("int", customerId.NativeType);
        Assert.Equal(typeof(int), customerId.SystemType);
        Assert.Equal(System.Data.ParameterDirection.Input, customerId.Direction);

        var minDate = cmd.Parameters.First(p => p.Name == "@MinDate");
        Assert.Equal("datetime2", minDate.NativeType);
    }

    [Fact]
    public void GetCustomerCount_HasOutputParameter()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerCount");
        var outputParam = cmd.Parameters.First(p => p.Name == "@Count");
        Assert.Equal(System.Data.ParameterDirection.Output, outputParam.Direction);
    }

    [Fact]
    public void PurgeOldOrders_HasNoInputParameters()
    {
        var cmd = _schema.Commands.First(c => c.Name == "PurgeOldOrders");
        var inputParams = cmd.Parameters.Where(p => p.Direction == System.Data.ParameterDirection.Input).ToList();
        Assert.Empty(inputParams);
    }

    [Fact]
    public void Commands_HaveReturnValueParameter()
    {
        foreach (var cmd in _schema.Commands)
        {
            Assert.NotNull(cmd.ReturnValueParameter);
            Assert.Equal(System.Data.ParameterDirection.ReturnValue, cmd.ReturnValueParameter.Direction);
        }
    }

    [Fact]
    public void Procedures_HaveIntReturnValue()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        Assert.Equal("int", cmd.ReturnValueParameter.NativeType);
    }

    [Fact]
    public void ScalarFunction_HasTypedReturnValue()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerBalance");
        Assert.Equal("decimal", cmd.ReturnValueParameter.NativeType);
    }

    [Fact]
    public void GetCustomerOrders_HasCommandResults()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        Assert.Single(cmd.CommandResults);
        Assert.Equal(3, cmd.CommandResults[0].Columns.Count);
    }

    [Fact]
    public void GetCustomerOrders_ResultColumns_HaveCorrectTypes()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        var result = cmd.CommandResults[0];

        var orderId = result.Columns.First(c => c.Name == "OrderId");
        Assert.Equal("int", orderId.NativeType);
        Assert.Equal(typeof(int), orderId.SystemType);

        var total = result.Columns.First(c => c.Name == "Total");
        Assert.Equal("money", total.NativeType);
    }

    [Fact]
    public void GetCustomerCount_HasNoCommandResults()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerCount");
        Assert.Empty(cmd.CommandResults);
    }

    [Fact]
    public void GetCustomerBalance_IsScalarFunction()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerBalance");
        Assert.True(cmd.ExtendedProperties.Contains("CS_IsScalarFunction"));
        Assert.Equal("true", cmd.ExtendedProperties["CS_IsScalarFunction"].Value.ToString());
    }

    [Fact]
    public void GetCustomerOrders_IsNotScalarFunction()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        Assert.True(cmd.ExtendedProperties.Contains("CS_IsScalarFunction"));
        Assert.Equal("false", cmd.ExtendedProperties["CS_IsScalarFunction"].Value.ToString());
    }

    [Fact]
    public void Parameters_HaveCorrectFullName()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        var param = cmd.Parameters.First(p => p.Name == "@CustomerId");
        Assert.Equal("dbo.GetCustomerOrders.@CustomerId", param.FullName);
    }

    [Fact]
    public void Parameters_HaveDatabaseReference()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        Assert.All(cmd.Parameters, p => Assert.Same(_schema, p.Database));
    }

    [Fact]
    public void CommandResultColumns_HaveDatabaseReference()
    {
        var cmd = _schema.Commands.First(c => c.Name == "GetCustomerOrders");
        var result = cmd.CommandResults[0];
        Assert.All(result.Columns, c => Assert.Same(_schema, c.Database));
    }

    // Phase 10: Cross-cutting
    [Fact]
    public void AllSchemaObjects_HaveNonNullDatabase()
    {
        foreach (var table in _schema.Tables)
        {
            Assert.NotNull(table.Database);
            foreach (var col in table.Columns)
                Assert.NotNull(col.Database);
            foreach (var fk in table.ForeignKeys)
                Assert.NotNull(fk.Database);
        }
        foreach (var view in _schema.Views)
        {
            Assert.NotNull(view.Database);
            foreach (var col in view.Columns)
                Assert.NotNull(col.Database);
        }
        foreach (var cmd in _schema.Commands)
        {
            Assert.NotNull(cmd.Database);
            foreach (var param in cmd.Parameters)
                Assert.NotNull(param.Database);
        }
    }

    [Fact]
    public void AllMemberColumns_HaveColumnReference()
    {
        foreach (var table in _schema.Tables)
        {
            if (table.PrimaryKey != null)
            {
                foreach (var mc in table.PrimaryKey.MemberColumns)
                    Assert.NotNull(mc.Column);
            }
            foreach (var fk in table.ForeignKeys)
            {
                foreach (var mc in fk.ForeignKeyMemberColumns)
                    Assert.NotNull(mc.Column);
                foreach (var mc in fk.PrimaryKeyMemberColumns)
                    Assert.NotNull(mc.Column);
            }
        }
    }

    [Fact]
    public void AllFullNames_AreNonEmpty()
    {
        foreach (var table in _schema.Tables)
        {
            Assert.NotEmpty(table.FullName);
            foreach (var col in table.Columns)
                Assert.NotEmpty(col.FullName);
        }
        foreach (var view in _schema.Views)
        {
            Assert.NotEmpty(view.FullName);
            foreach (var col in view.Columns)
                Assert.NotEmpty(col.FullName);
        }
        foreach (var cmd in _schema.Commands)
        {
            Assert.NotEmpty(cmd.FullName);
            foreach (var param in cmd.Parameters)
                Assert.NotEmpty(param.FullName);
        }
    }

    [Fact]
    public void TableSchema_GetTableData_ThrowsNotImplementedException()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        Assert.Throws<NotImplementedException>(() => table.GetTableData());
    }

    [Fact]
    public void MemberColumnSchemaCollection_Contains_WorksByName()
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        Assert.True(table.PrimaryKey.MemberColumns.Contains("CustomerId"));
        Assert.False(table.PrimaryKey.MemberColumns.Contains("NonExistent"));
    }

    // Helper
    private ColumnSchema GetCustomerColumn(string columnName)
    {
        var table = _schema.Tables.First(t => t.Name == "Customer");
        return table.Columns.First(c => c.Name == columnName);
    }
}
