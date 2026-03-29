using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.Data.SqlClient;
using SchemaExplorer;
using Testcontainers.MsSql;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks SQL Server schema introspection. Requires Docker.
/// Run with: dotnet run -c Release -- --filter "*SqlSchema*"
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 3)]
public class SqlSchemaProviderBenchmarks
{
    private MsSqlContainer _container = null!;
    private string _connectionString = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        await SeedDatabaseAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _container.DisposeAsync();
    }

    [Benchmark]
    public DatabaseSchema GetDatabaseSchema()
    {
        var provider = new SqlSchemaProvider();
        return provider.GetDatabaseSchema(_connectionString);
    }

    private async Task SeedDatabaseAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var commands = SeedSql.Split("GO", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var commandText in commands)
        {
            if (string.IsNullOrWhiteSpace(commandText))
                continue;

            await using var cmd = new SqlCommand(commandText, connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private const string SeedSql = """
        CREATE SCHEMA [Sales];
        GO

        CREATE TABLE [dbo].[Customer] (
            [CustomerId]   INT              NOT NULL IDENTITY(1,1),
            [FirstName]    NVARCHAR(100)    NOT NULL,
            [LastName]     NVARCHAR(100)    NOT NULL,
            [Email]        NVARCHAR(255)    NULL,
            [BirthDate]    DATE             NULL,
            [Balance]      DECIMAL(18,2)    NOT NULL DEFAULT 0,
            [IsActive]     BIT              NOT NULL DEFAULT 1,
            [RowGuid]      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
            [Photo]        VARBINARY(MAX)   NULL,
            CONSTRAINT [PK_Customer] PRIMARY KEY ([CustomerId])
        );
        GO

        CREATE UNIQUE INDEX [UX_Customer_Email] ON [dbo].[Customer]([Email]) WHERE [Email] IS NOT NULL;
        GO

        EXEC sp_addextendedproperty
            @name=N'MS_Description', @value=N'Main customer table',
            @level0type=N'SCHEMA', @level0name=N'dbo',
            @level1type=N'TABLE',  @level1name=N'Customer';
        GO

        CREATE TABLE [Sales].[Order] (
            [OrderId]    INT       NOT NULL IDENTITY(1,1),
            [CustomerId] INT       NOT NULL,
            [OrderDate]  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
            [Total]      MONEY     NOT NULL,
            CONSTRAINT [PK_Order] PRIMARY KEY ([OrderId]),
            CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId])
                REFERENCES [dbo].[Customer]([CustomerId]) ON DELETE CASCADE
        );
        GO

        CREATE TABLE [dbo].[OrderItem] (
            [OrderId]   INT           NOT NULL,
            [ProductId] INT           NOT NULL,
            [Quantity]  SMALLINT      NOT NULL DEFAULT 1,
            [UnitPrice] DECIMAL(18,2) NOT NULL,
            CONSTRAINT [PK_OrderItem] PRIMARY KEY ([OrderId], [ProductId])
        );
        GO

        CREATE TABLE [dbo].[OrderItemNote] (
            [NoteId]    INT           NOT NULL IDENTITY(1,1),
            [OrderId]   INT           NOT NULL,
            [ProductId] INT           NOT NULL,
            [Note]      NVARCHAR(MAX) NOT NULL,
            CONSTRAINT [PK_OrderItemNote] PRIMARY KEY ([NoteId]),
            CONSTRAINT [FK_OrderItemNote_OrderItem] FOREIGN KEY ([OrderId], [ProductId])
                REFERENCES [dbo].[OrderItem]([OrderId], [ProductId])
        );
        GO

        CREATE TABLE [dbo].[AuditLog] (
            [LogId]     BIGINT          NOT NULL IDENTITY(1,1),
            [Message]   NVARCHAR(MAX)   NOT NULL,
            [CreatedAt] DATETIMEOFFSET  NOT NULL DEFAULT SYSDATETIMEOFFSET()
        );
        GO

        CREATE VIEW [dbo].[ActiveCustomers] AS
            SELECT [CustomerId], [FirstName], [LastName], [Email]
            FROM [dbo].[Customer]
            WHERE [IsActive] = 1;
        GO

        CREATE VIEW [Sales].[OrderSummary] AS
            SELECT o.[OrderId], c.[FirstName], c.[LastName], o.[Total], o.[OrderDate]
            FROM [Sales].[Order] o
            JOIN [dbo].[Customer] c ON o.[CustomerId] = c.[CustomerId];
        GO

        CREATE PROCEDURE [dbo].[GetCustomerOrders]
            @CustomerId INT,
            @MinDate    DATETIME2 = NULL
        AS
        BEGIN
            SELECT o.[OrderId], o.[OrderDate], o.[Total]
            FROM [Sales].[Order] o
            WHERE o.[CustomerId] = @CustomerId
              AND (@MinDate IS NULL OR o.[OrderDate] >= @MinDate);
        END;
        GO

        CREATE PROCEDURE [dbo].[GetCustomerCount]
            @Count INT OUTPUT
        AS
        BEGIN
            SELECT @Count = COUNT(*) FROM [dbo].[Customer];
        END;
        GO

        CREATE FUNCTION [dbo].[GetCustomerBalance](@CustomerId INT)
        RETURNS DECIMAL(18,2)
        AS
        BEGIN
            RETURN (SELECT [Balance] FROM [dbo].[Customer] WHERE [CustomerId] = @CustomerId);
        END;
        GO
        """;
}
