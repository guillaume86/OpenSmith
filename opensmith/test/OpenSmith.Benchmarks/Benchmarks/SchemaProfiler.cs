using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.Data.SqlClient;
using SchemaExplorer;

namespace OpenSmith.Benchmarks.Benchmarks;

/// <summary>
/// Profile GetDatabaseSchema against a real SQL Server instance.
/// Run with: dotnet run -c Release -- --filter "*SchemaProfiler*"
/// Set OPENSMITH_CONN to override the connection string.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.ColdStart, iterationCount: 1)]
public class SchemaProfiler
{
    private string _connectionString = null!;

    [GlobalSetup]
    public void Setup()
    {
        _connectionString = Environment.GetEnvironmentVariable("OPENSMITH_CONN")
            ?? @"Data Source=.\SQL2014;Initial Catalog=HRWeb;Integrated Security=True;TrustServerCertificate=True";
        Console.WriteLine($"[SchemaProfiler] Using: {_connectionString}");
    }

    [Benchmark]
    public DatabaseSchema GetDatabaseSchema()
    {
        var provider = new SqlSchemaProvider();
        return provider.GetDatabaseSchema(_connectionString);
    }

    /// <summary>
    /// Run this directly (not via BenchmarkDotNet) for detailed per-step timing.
    /// Invoke: dotnet run -c Release -- profile-schema [connection-string]
    /// </summary>
    public static void RunDetailed(string? connectionStringOverride = null)
    {
        var connStr = connectionStringOverride
            ?? Environment.GetEnvironmentVariable("OPENSMITH_CONN")
            ?? @"Data Source=.\SQL2014;Initial Catalog=HRWeb;Integrated Security=True;TrustServerCertificate=True";

        Console.WriteLine($"Connection: {connStr}");
        Console.WriteLine();

        var totalSw = Stopwatch.StartNew();
        var sw = new Stopwatch();

        // Open connection
        sw.Start();
        using var connection = new SqlConnection(connStr);
        connection.Open();
        sw.Stop();
        Console.WriteLine($"  Connection.Open(): {sw.Elapsed.TotalSeconds:0.00}s");

        var dbName = new SqlCommand("SELECT DB_NAME()", connection).ExecuteScalar()?.ToString()
            ?? connection.Database;

        var db = new DatabaseSchema
        {
            Name = dbName,
            ConnectionString = connStr,
            Provider = new DatabaseProvider { Name = "SqlSchemaProvider" }
        };

        var tableIndex = new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase);
        var columnIndex = new Dictionary<string, ColumnSchema>(StringComparer.OrdinalIgnoreCase);

        // Use reflection to call each private Load* method with timing
        var providerType = typeof(SqlSchemaProvider);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

        void TimeStep(string name, Action action)
        {
            sw.Restart();
            action();
            sw.Stop();
            Console.WriteLine($"  {name}: {sw.Elapsed.TotalSeconds:0.00}s");
        }

        TimeStep("LoadTables", () =>
            providerType.GetMethod("LoadTables", flags)!.Invoke(null, [connection, db, tableIndex]));
        Console.WriteLine($"    → {db.Tables.Count} tables");

        TimeStep("LoadTableColumns", () =>
            providerType.GetMethod("LoadTableColumns", flags)!.Invoke(null, [connection, db, tableIndex, columnIndex]));
        Console.WriteLine($"    → {columnIndex.Count} columns");

        TimeStep("LoadPrimaryKeys", () =>
            providerType.GetMethod("LoadPrimaryKeys", flags)!.Invoke(null, [connection, tableIndex, columnIndex]));

        TimeStep("LoadUniqueConstraints", () =>
            providerType.GetMethod("LoadUniqueConstraints", flags)!.Invoke(null, [connection, tableIndex]));

        TimeStep("LoadForeignKeys", () =>
            providerType.GetMethod("LoadForeignKeys", flags)!.Invoke(null, [connection, db, tableIndex, columnIndex]));

        TimeStep("LoadExtendedProperties", () =>
            providerType.GetMethod("LoadExtendedProperties", flags)!.Invoke(null, [connection, tableIndex, columnIndex]));

        TimeStep("LoadSyntheticColumnProperties", () =>
            providerType.GetMethod("LoadSyntheticColumnProperties", flags)!.Invoke(null, [connection, tableIndex]));

        TimeStep("LoadCascadeDeleteProperties", () =>
            providerType.GetMethod("LoadCascadeDeleteProperties", flags)!.Invoke(null, [connection, tableIndex]));

        TimeStep("LoadViews", () =>
            providerType.GetMethod("LoadViews", flags)!.Invoke(null, [connection, db]));
        Console.WriteLine($"    → {db.Views.Count} views");

        TimeStep("LoadViewColumns", () =>
            providerType.GetMethod("LoadViewColumns", flags)!.Invoke(null, [connection, db]));

        TimeStep("LoadCommands (deepLoad=false)", () =>
            providerType.GetMethod("LoadCommands", flags)!.Invoke(null, [connection, db, false]));
        Console.WriteLine($"    → {db.Commands.Count} commands");

        totalSw.Stop();
        Console.WriteLine();
        Console.WriteLine($"  TOTAL: {totalSw.Elapsed.TotalSeconds:0.00}s");
    }
}
