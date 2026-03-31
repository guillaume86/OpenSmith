using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Data.SqlClient;

namespace SchemaExplorer;

public class SqlSchemaProvider
{
    /// <summary>
    /// When true, use the transactional fallback strategy to introspect stored procedure result sets
    /// by actually executing them inside a rolled-back transaction. This is expensive for databases
    /// with heavy procedures. When false, only the fast SchemaOnly strategy is used. Default: false.
    /// </summary>
    public bool DeepLoad { get; set; }

    /// <summary>
    /// When true, include database views in the schema. Default: false.
    /// </summary>
    public bool IncludeViews { get; set; }

    /// <summary>
    /// When true, include stored procedures and functions in the schema. Default: false.
    /// </summary>
    public bool IncludeFunctions { get; set; }

    private static readonly Dictionary<string, (Type SystemType, DbType DbType)> TypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = (typeof(int), DbType.Int32),
        ["bigint"] = (typeof(long), DbType.Int64),
        ["smallint"] = (typeof(short), DbType.Int16),
        ["tinyint"] = (typeof(byte), DbType.Byte),
        ["bit"] = (typeof(bool), DbType.Boolean),
        ["decimal"] = (typeof(decimal), DbType.Decimal),
        ["numeric"] = (typeof(decimal), DbType.Decimal),
        ["float"] = (typeof(double), DbType.Double),
        ["real"] = (typeof(float), DbType.Single),
        ["money"] = (typeof(decimal), DbType.Currency),
        ["smallmoney"] = (typeof(decimal), DbType.Currency),
        ["nvarchar"] = (typeof(string), DbType.String),
        ["nchar"] = (typeof(string), DbType.StringFixedLength),
        ["ntext"] = (typeof(string), DbType.String),
        ["varchar"] = (typeof(string), DbType.AnsiString),
        ["char"] = (typeof(string), DbType.AnsiStringFixedLength),
        ["text"] = (typeof(string), DbType.AnsiString),
        ["datetime"] = (typeof(DateTime), DbType.DateTime),
        ["datetime2"] = (typeof(DateTime), DbType.DateTime2),
        ["smalldatetime"] = (typeof(DateTime), DbType.DateTime),
        ["date"] = (typeof(DateTime), DbType.Date),
        ["time"] = (typeof(TimeSpan), DbType.Time),
        ["datetimeoffset"] = (typeof(DateTimeOffset), DbType.DateTimeOffset),
        ["uniqueidentifier"] = (typeof(Guid), DbType.Guid),
        ["varbinary"] = (typeof(byte[]), DbType.Binary),
        ["binary"] = (typeof(byte[]), DbType.Binary),
        ["image"] = (typeof(byte[]), DbType.Binary),
        ["xml"] = (typeof(XmlDocument), DbType.Xml),
        ["timestamp"] = (typeof(byte[]), DbType.Binary),
        ["rowversion"] = (typeof(byte[]), DbType.Binary),
        ["sql_variant"] = (typeof(object), DbType.Object),
    };

    // ── Raw-data records (returned by Fetch methods, consumed by Apply methods) ──

    private record struct RawPrimaryKey(string SchemaName, string TableName, string ColumnName, int KeyOrdinal);
    private record struct RawUniqueColumn(string SchemaName, string TableName, string ColumnName);
    private record struct RawForeignKey(string FKName, string FKSchema, string FKTable, string FKColumn,
        string PKSchema, string PKTable, string PKColumn);
    private record struct RawCascadeDelete(string FKName, string FKSchema, string FKTable, byte DeleteAction);
    private record struct RawExtendedProperty(string SchemaName, string ObjectName, string ColumnName,
        string PropertyName, object PropertyValue);
    private record struct RawSyntheticColumn(string SchemaName, string TableName, string ColumnName,
        bool IsIdentity, bool IsComputed);

    // ── Orchestration ────────────────────────────────────────────────────────────

    public DatabaseSchema GetDatabaseSchema(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();

        // Use DB_NAME() to get the actual database name as stored in sys.databases,
        // rather than connection.Database which returns the Initial Catalog value from the connection string
        var dbName = new SqlCommand("SELECT DB_NAME()", connection).ExecuteScalar()?.ToString()
            ?? connection.Database;

        var db = new DatabaseSchema
        {
            Name = dbName,
            ConnectionString = connectionString,
            Provider = new DatabaseProvider { Name = "SqlSchemaProvider" }
        };

        // Index: "schema.table" -> TableSchema
        var tableIndex = new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase);
        // Index: "schema.table.column" -> ColumnSchema
        var columnIndex = new Dictionary<string, ColumnSchema>(StringComparer.OrdinalIgnoreCase);

        // Determine how many extra connections we need for the parallel phase.
        // Open them now so they warm up while Phase 1 runs on the main connection.
        int extraConnCount = 4; // PK, Unique, FK+Cascade, ExtProps+Synthetic
        if (IncludeViews) extraConnCount++;
        if (IncludeFunctions) extraConnCount++;

        var extraConns = new SqlConnection[extraConnCount];
        var openTasks = new Task[extraConnCount];
        for (int i = 0; i < extraConnCount; i++)
        {
            extraConns[i] = new SqlConnection(connectionString);
            openTasks[i] = extraConns[i].OpenAsync();
        }

        // Phase 1: Build lookup indexes (sequential — later phases depend on these).
        // Extra connections open in the background while this runs.
        LoadTables(connection, db, tableIndex);
        LoadTableColumns(connection, db, tableIndex, columnIndex);

        // Ensure all extra connections are ready
        Task.WaitAll(openTasks);

        try
        {
            // Phase 2: Parallel SQL fetches — each task returns raw data, no shared
            // mutable state is read or written, so no ordering constraints exist.
            int ci = 0;

            List<RawPrimaryKey> rawPKs = null;
            List<RawUniqueColumn> rawUniques = null;
            List<RawForeignKey> rawFKs = null;
            List<RawCascadeDelete> rawCascades = null;
            List<RawExtendedProperty> rawExtProps = null;
            List<RawSyntheticColumn> rawSynthetics = null;

            var tasks = new List<Task>();

            var connPk = extraConns[ci++];
            tasks.Add(Task.Run(() => { rawPKs = FetchPrimaryKeys(connPk); }));

            var connUnique = extraConns[ci++];
            tasks.Add(Task.Run(() => { rawUniques = FetchUniqueConstraints(connUnique); }));

            var connFk = extraConns[ci++];
            tasks.Add(Task.Run(() =>
            {
                rawFKs = FetchForeignKeys(connFk);
                rawCascades = FetchCascadeDelete(connFk);
            }));

            var connExtProps = extraConns[ci++];
            tasks.Add(Task.Run(() =>
            {
                rawExtProps = FetchExtendedProperties(connExtProps);
                rawSynthetics = FetchSyntheticColumns(connExtProps);
            }));

            if (IncludeViews)
            {
                var connViews = extraConns[ci++];
                tasks.Add(Task.Run(() =>
                {
                    LoadViews(connViews, db);
                    LoadViewColumns(connViews, db);
                }));
            }

            if (IncludeFunctions)
            {
                var connCmds = extraConns[ci++];
                tasks.Add(Task.Run(() =>
                {
                    LoadCommands(connCmds, db, DeepLoad);
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Phase 3: Sequential object-graph build from raw data.
            // No ordering constraints — each Apply touches disjoint properties —
            // but PK/Unique before FK is clearest.
            ApplyPrimaryKeys(rawPKs, tableIndex, columnIndex);
            ApplyUniqueConstraints(rawUniques, tableIndex);
            ApplyForeignKeys(rawFKs, db, tableIndex, columnIndex);
            ApplyCascadeDelete(rawCascades, tableIndex);
            ApplyExtendedProperties(rawExtProps, tableIndex, columnIndex);
            ApplySyntheticColumns(rawSynthetics, tableIndex);
        }
        finally
        {
            foreach (var c in extraConns)
                c.Dispose();
        }

        return db;
    }

    // ── Phase 1: Table & column loading (sequential, builds indexes) ─────────────

    private static void LoadTables(SqlConnection connection, DatabaseSchema db, Dictionary<string, TableSchema> tableIndex)
    {
        const string sql = """
            SELECT t.name AS TableName, SCHEMA_NAME(t.schema_id) AS SchemaName
            FROM sys.tables t
            WHERE t.is_ms_shipped = 0
            ORDER BY SchemaName, TableName
            """;

        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var schemaName = reader.GetString(1);
            var tableName = reader.GetString(0);
            var table = new TableSchema
            {
                Name = tableName,
                FullName = $"{schemaName}.{tableName}",
                Database = db
            };
            db.Tables.Add(table);
            tableIndex[table.FullName] = table;
        }
    }

    private static void LoadTableColumns(SqlConnection connection, DatabaseSchema db,
        Dictionary<string, TableSchema> tableIndex, Dictionary<string, ColumnSchema> columnIndex)
    {
        const string sql = """
            SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,
                   t.name AS TableName,
                   c.name AS ColumnName,
                   tp.name AS NativeType,
                   CAST(CASE WHEN tp.name IN ('nchar', 'nvarchar') AND c.max_length > 0
                        THEN c.max_length / 2
                        ELSE c.max_length END AS smallint) AS Size,
                   c.precision AS [Precision],
                   c.scale AS Scale,
                   c.is_nullable AS AllowDBNull
            FROM sys.columns c
            JOIN sys.tables t ON c.object_id = t.object_id
            JOIN sys.types tp ON c.user_type_id = tp.user_type_id
            WHERE t.is_ms_shipped = 0
            ORDER BY SchemaName, TableName, c.column_id
            """;

        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var schemaName = reader.GetString(0);
            var tableName = reader.GetString(1);
            var tableKey = $"{schemaName}.{tableName}";

            if (!tableIndex.TryGetValue(tableKey, out var table))
                continue;

            var columnName = reader.GetString(2);
            var nativeType = reader.GetString(3);
            var (systemType, dbType) = ResolveType(nativeType);

            var column = new ColumnSchema
            {
                Name = columnName,
                FullName = $"{tableKey}.{columnName}",
                Database = db,
                Table = table,
                NativeType = nativeType,
                SystemType = systemType,
                DataType = dbType,
                Size = reader.GetInt16(4),
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6),
                AllowDBNull = reader.GetBoolean(7)
            };
            table.Columns.Add(column);
            columnIndex[column.FullName] = column;
        }
    }

    // ── Phase 2: Fetch methods (pure SQL → raw data, no side effects) ────────────

    private static List<RawPrimaryKey> FetchPrimaryKeys(SqlConnection connection)
    {
        const string sql = """
            SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,
                   t.name AS TableName,
                   c.name AS ColumnName,
                   ic.key_ordinal AS KeyOrdinal
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            JOIN sys.tables t ON i.object_id = t.object_id
            WHERE i.is_primary_key = 1
            ORDER BY SchemaName, TableName, ic.key_ordinal
            """;

        var results = new List<RawPrimaryKey>();
        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new RawPrimaryKey(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3)));
        return results;
    }

    private static List<RawUniqueColumn> FetchUniqueConstraints(SqlConnection connection)
    {
        const string sql = """
            SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,
                   t.name AS TableName,
                   c.name AS ColumnName
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            JOIN sys.tables t ON i.object_id = t.object_id
            WHERE i.is_unique = 1
              AND i.is_primary_key = 0
              AND (SELECT COUNT(*) FROM sys.index_columns ic2
                   WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id) = 1
            """;

        var results = new List<RawUniqueColumn>();
        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new RawUniqueColumn(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return results;
    }

    private static List<RawForeignKey> FetchForeignKeys(SqlConnection connection)
    {
        const string sql = """
            SELECT fk.name AS FKName,
                   SCHEMA_NAME(fkt.schema_id) AS FKSchemaName,
                   fkt.name AS FKTableName,
                   fkc.name AS FKColumnName,
                   SCHEMA_NAME(pkt.schema_id) AS PKSchemaName,
                   pkt.name AS PKTableName,
                   pkc.name AS PKColumnName,
                   fkcc.constraint_column_id AS Ordinal
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkcc ON fk.object_id = fkcc.constraint_object_id
            JOIN sys.tables fkt ON fkcc.parent_object_id = fkt.object_id
            JOIN sys.columns fkc ON fkcc.parent_object_id = fkc.object_id AND fkcc.parent_column_id = fkc.column_id
            JOIN sys.tables pkt ON fkcc.referenced_object_id = pkt.object_id
            JOIN sys.columns pkc ON fkcc.referenced_object_id = pkc.object_id AND fkcc.referenced_column_id = pkc.column_id
            ORDER BY FKName, Ordinal
            """;

        var results = new List<RawForeignKey>();
        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new RawForeignKey(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        return results;
    }

    private static List<RawCascadeDelete> FetchCascadeDelete(SqlConnection connection)
    {
        const string sql = """
            SELECT fk.name AS FKName,
                   SCHEMA_NAME(fkt.schema_id) AS FKSchemaName,
                   fkt.name AS FKTableName,
                   fk.delete_referential_action AS DeleteAction
            FROM sys.foreign_keys fk
            JOIN sys.tables fkt ON fk.parent_object_id = fkt.object_id
            """;

        var results = new List<RawCascadeDelete>();
        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new RawCascadeDelete(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3)));
        return results;
    }

    private static List<RawExtendedProperty> FetchExtendedProperties(SqlConnection connection)
    {
        const string sql = """
            SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,
                   t.name AS ObjectName,
                   c.name AS ColumnName,
                   ep.name AS PropertyName,
                   ep.value AS PropertyValue
            FROM sys.extended_properties ep
            JOIN sys.tables t ON ep.major_id = t.object_id AND ep.class = 1
            LEFT JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.minor_id > 0
            ORDER BY SchemaName, ObjectName, ColumnName, PropertyName
            """;

        var results = new List<RawExtendedProperty>();
        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new RawExtendedProperty(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.GetValue(4)));
        return results;
    }

    private static List<RawSyntheticColumn> FetchSyntheticColumns(SqlConnection connection)
    {
        const string sql = """
            SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,
                   t.name AS TableName,
                   c.name AS ColumnName,
                   c.is_identity AS IsIdentity,
                   c.is_computed AS IsComputed
            FROM sys.columns c
            JOIN sys.tables t ON c.object_id = t.object_id
            WHERE t.is_ms_shipped = 0
            """;

        var results = new List<RawSyntheticColumn>();
        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new RawSyntheticColumn(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetBoolean(3), reader.GetBoolean(4)));
        return results;
    }

    // ── Phase 3: Apply methods (object-graph mutation, no I/O) ───────────────────

    private static void ApplyPrimaryKeys(List<RawPrimaryKey> rows,
        Dictionary<string, TableSchema> tableIndex, Dictionary<string, ColumnSchema> columnIndex)
    {
        foreach (var row in rows)
        {
            var tableKey = $"{row.SchemaName}.{row.TableName}";

            if (!tableIndex.TryGetValue(tableKey, out var table))
                continue;

            if (table.PrimaryKey == null)
            {
                table.PrimaryKey = new PrimaryKeySchema();
                table.HasPrimaryKey = true;
            }

            var colKey = $"{tableKey}.{row.ColumnName}";
            columnIndex.TryGetValue(colKey, out var sourceColumn);

            if (sourceColumn != null)
                sourceColumn.IsPrimaryKeyMember = true;

            // MemberColumns are added in key_ordinal order (rows come sorted by
            // key_ordinal from the SQL query), which is what FK correspondence needs.
            // table.Columns is intentionally NOT reordered — it must stay in column_id
            // order to match CodeSmith behavior.
            var memberColumn = CreateMemberColumn(sourceColumn, row.ColumnName);
            memberColumn.IsPrimaryKeyMember = true;
            table.PrimaryKey.MemberColumns.Add(memberColumn);
        }
    }

    private static void ApplyUniqueConstraints(List<RawUniqueColumn> rows,
        Dictionary<string, TableSchema> tableIndex)
    {
        foreach (var row in rows)
        {
            var tableKey = $"{row.SchemaName}.{row.TableName}";

            if (!tableIndex.TryGetValue(tableKey, out var table))
                continue;

            var column = FindColumn(table, row.ColumnName);
            if (column != null)
                column.IsUnique = true;
        }
    }

    private static void ApplyForeignKeys(List<RawForeignKey> rows, DatabaseSchema db,
        Dictionary<string, TableSchema> tableIndex, Dictionary<string, ColumnSchema> columnIndex)
    {
        // Group FK columns by FK name
        var fkData = new Dictionary<string, (string FKSchema, string FKTable, string PKSchema, string PKTable,
            List<string> FKColumns, List<string> PKColumns)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!fkData.ContainsKey(row.FKName))
                fkData[row.FKName] = (row.FKSchema, row.FKTable, row.PKSchema, row.PKTable, new List<string>(), new List<string>());

            fkData[row.FKName].FKColumns.Add(row.FKColumn);
            fkData[row.FKName].PKColumns.Add(row.PKColumn);
        }

        foreach (var (fkName, data) in fkData)
        {
            var fkTableKey = $"{data.FKSchema}.{data.FKTable}";
            var pkTableKey = $"{data.PKSchema}.{data.PKTable}";

            if (!tableIndex.TryGetValue(fkTableKey, out var fkTable) ||
                !tableIndex.TryGetValue(pkTableKey, out var pkTable))
                continue;

            var tableKey = new TableKeySchema
            {
                Name = fkName,
                FullName = $"{data.FKSchema}.{fkName}",
                Database = db,
                ForeignKeyTable = fkTable,
                PrimaryKeyTable = pkTable
            };

            for (int i = 0; i < data.FKColumns.Count; i++)
            {
                var fkColKey = $"{fkTableKey}.{data.FKColumns[i]}";
                columnIndex.TryGetValue(fkColKey, out var fkSourceCol);
                if (fkSourceCol != null)
                    fkSourceCol.IsForeignKeyMember = true;

                var fkMember = CreateMemberColumn(fkSourceCol, data.FKColumns[i]);
                fkMember.IsForeignKeyMember = true;
                tableKey.ForeignKeyMemberColumns.Add(fkMember);

                var pkColKey = $"{pkTableKey}.{data.PKColumns[i]}";
                columnIndex.TryGetValue(pkColKey, out var pkSourceCol);

                var pkMember = CreateMemberColumn(pkSourceCol, data.PKColumns[i]);
                tableKey.PrimaryKeyMemberColumns.Add(pkMember);
            }

            fkTable.ForeignKeys.Add(tableKey);
            pkTable.PrimaryKeys.Add(tableKey);
        }
    }

    private static void ApplyCascadeDelete(List<RawCascadeDelete> rows,
        Dictionary<string, TableSchema> tableIndex)
    {
        foreach (var row in rows)
        {
            var tableKey = $"{row.FKSchema}.{row.FKTable}";
            if (!tableIndex.TryGetValue(tableKey, out var table))
                continue;

            foreach (var fk in table.ForeignKeys)
            {
                if (fk.Name == row.FKName)
                {
                    fk.ExtendedProperties.Add(new ExtendedProperty
                    {
                        Name = "CS_CascadeDelete",
                        Value = row.DeleteAction == 1 // CASCADE
                    });
                    break;
                }
            }
        }
    }

    private static void ApplyExtendedProperties(List<RawExtendedProperty> rows,
        Dictionary<string, TableSchema> tableIndex, Dictionary<string, ColumnSchema> columnIndex)
    {
        foreach (var row in rows)
        {
            var tableKey = $"{row.SchemaName}.{row.ObjectName}";

            if (row.ColumnName != null)
            {
                var colKey = $"{tableKey}.{row.ColumnName}";
                if (columnIndex.TryGetValue(colKey, out var column))
                    column.ExtendedProperties.Add(new ExtendedProperty { Name = row.PropertyName, Value = row.PropertyValue });
            }
            else
            {
                if (tableIndex.TryGetValue(tableKey, out var table))
                    table.ExtendedProperties.Add(new ExtendedProperty { Name = row.PropertyName, Value = row.PropertyValue });
            }
        }
    }

    private static void ApplySyntheticColumns(List<RawSyntheticColumn> rows,
        Dictionary<string, TableSchema> tableIndex)
    {
        foreach (var row in rows)
        {
            var tableKey = $"{row.SchemaName}.{row.TableName}";
            if (!tableIndex.TryGetValue(tableKey, out var table))
                continue;

            var column = FindColumn(table, row.ColumnName);
            if (column == null)
                continue;

            column.ExtendedProperties.Add(new ExtendedProperty
            {
                Name = "CS_IsIdentity",
                Value = row.IsIdentity.ToString().ToLower()
            });
            column.ExtendedProperties.Add(new ExtendedProperty
            {
                Name = "CS_IsComputed",
                Value = row.IsComputed.ToString().ToLower()
            });
        }
    }

    // ── Self-contained loaders (Views, Commands — no cross-dependencies) ─────────

    private static void LoadViews(SqlConnection connection, DatabaseSchema db)
    {
        const string sql = """
            SELECT v.name AS ViewName, SCHEMA_NAME(v.schema_id) AS SchemaName
            FROM sys.views v
            WHERE v.is_ms_shipped = 0
            ORDER BY SchemaName, ViewName
            """;

        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var viewName = reader.GetString(0);
            var schemaName = reader.GetString(1);
            db.Views.Add(new ViewSchema
            {
                Name = viewName,
                FullName = $"{schemaName}.{viewName}",
                Database = db
            });
        }
    }

    private static void LoadViewColumns(SqlConnection connection, DatabaseSchema db)
    {
        const string sql = """
            SELECT SCHEMA_NAME(v.schema_id) AS SchemaName,
                   v.name AS ViewName,
                   c.name AS ColumnName,
                   tp.name AS NativeType,
                   CAST(CASE WHEN tp.name IN ('nchar', 'nvarchar') AND c.max_length > 0
                        THEN c.max_length / 2
                        ELSE c.max_length END AS smallint) AS Size,
                   c.precision AS [Precision],
                   c.scale AS Scale,
                   c.is_nullable AS AllowDBNull
            FROM sys.columns c
            JOIN sys.views v ON c.object_id = v.object_id
            JOIN sys.types tp ON c.user_type_id = tp.user_type_id
            WHERE v.is_ms_shipped = 0
            ORDER BY SchemaName, ViewName, c.column_id
            """;

        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var schemaName = reader.GetString(0);
            var viewName = reader.GetString(1);
            var viewFullName = $"{schemaName}.{viewName}";

            var view = db.Views.FirstOrDefault(v => v.FullName == viewFullName);
            if (view == null) continue;

            var columnName = reader.GetString(2);
            var nativeType = reader.GetString(3);
            var (systemType, dbType) = ResolveType(nativeType);

            view.Columns.Add(new ViewColumnSchema
            {
                Name = columnName,
                FullName = $"{viewFullName}.{columnName}",
                Database = db,
                NativeType = nativeType,
                SystemType = systemType,
                DataType = dbType,
                Size = reader.GetInt16(4),
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6),
                AllowDBNull = reader.GetBoolean(7)
            });
        }
    }

    private static void LoadCommands(SqlConnection connection, DatabaseSchema db, bool deepLoad = false)
    {
        // Load procedures and scalar functions
        // Exclude diagram-related system objects (sp_*diagram*, fn_diagramobjects)
        // which are user-created (is_ms_shipped=0) but are system utilities
        const string sql = """
            SELECT o.name AS ObjectName,
                   SCHEMA_NAME(o.schema_id) AS SchemaName,
                   o.type AS ObjectType
            FROM sys.objects o
            WHERE o.type IN ('P', 'FN', 'IF', 'TF')
              AND o.is_ms_shipped = 0
              AND o.name NOT LIKE 'sp_%diagram%'
              AND o.name NOT LIKE 'fn_%diagram%'
            ORDER BY SchemaName, ObjectName
            """;

        var commandTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using (var cmd = new SqlCommand(sql, connection))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var objectName = reader.GetString(0);
                var schemaName = reader.GetString(1);
                var objectType = reader.GetString(2).Trim();
                var fullName = $"{schemaName}.{objectName}";

                var command = new CommandSchema
                {
                    Name = objectName,
                    FullName = fullName,
                    Database = db
                };

                command.ExtendedProperties.Add(new ExtendedProperty
                {
                    Name = "CS_IsScalarFunction",
                    Value = (objectType == "FN").ToString().ToLower()
                });
                command.ExtendedProperties.Add(new ExtendedProperty
                {
                    Name = "CS_IsTableValuedFunction",
                    Value = (objectType == "IF" || objectType == "TF").ToString().ToLower()
                });

                db.Commands.Add(command);
                commandTypes[fullName] = objectType;
            }
        }

        LoadCommandParameters(connection, db);
        LoadCommandResults(connection, db, commandTypes, deepLoad);
    }

    private static void LoadCommandParameters(SqlConnection connection, DatabaseSchema db)
    {
        const string sql = """
            SELECT SCHEMA_NAME(o.schema_id) AS SchemaName,
                   o.name AS ObjectName,
                   par.name AS ParameterName,
                   tp.name AS NativeType,
                   CAST(CASE WHEN tp.name IN ('nchar', 'nvarchar') AND par.max_length > 0
                        THEN par.max_length / 2
                        ELSE par.max_length END AS smallint) AS Size,
                   par.precision AS [Precision],
                   par.scale AS Scale,
                   par.is_output AS IsOutput,
                   par.parameter_id AS Ordinal
            FROM sys.parameters par
            JOIN sys.objects o ON par.object_id = o.object_id
            JOIN sys.types tp ON par.user_type_id = tp.user_type_id
            WHERE o.type IN ('P', 'FN', 'IF', 'TF')
              AND o.is_ms_shipped = 0
            ORDER BY SchemaName, ObjectName, par.parameter_id
            """;

        using var cmd = new SqlCommand(sql, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var schemaName = reader.GetString(0);
            var objectName = reader.GetString(1);
            var paramName = reader.GetString(2);
            var nativeType = reader.GetString(3);
            var isOutput = reader.GetBoolean(7);
            var ordinal = reader.GetInt32(8);

            var fullName = $"{schemaName}.{objectName}";
            var command = db.Commands.FirstOrDefault(c => c.FullName == fullName);
            if (command == null) continue;

            var (systemType, dbType) = ResolveType(nativeType);

            ParameterDirection direction;
            if (ordinal == 0)
                direction = ParameterDirection.ReturnValue;
            else if (isOutput)
                direction = ParameterDirection.InputOutput; // SQL Server OUTPUT params are always bidirectional
            else
                direction = ParameterDirection.Input;

            var param = new ParameterSchema
            {
                Name = paramName,
                FullName = $"{fullName}.{paramName}",
                Database = db,
                NativeType = nativeType,
                SystemType = systemType,
                DataType = dbType,
                Size = reader.GetInt16(4),
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6),
                Direction = direction
            };

            if (direction == ParameterDirection.ReturnValue)
                command.ReturnValueParameter = param;
            else
                command.Parameters.Add(param);
        }

        // Ensure all commands have a ReturnValueParameter (procedures always return int)
        foreach (var command in db.Commands)
        {
            if (command.ReturnValueParameter == null)
            {
                command.ReturnValueParameter = new ParameterSchema
                {
                    Name = "@RETURN_VALUE",
                    FullName = $"{command.FullName}.@RETURN_VALUE",
                    Database = db,
                    NativeType = "int",
                    SystemType = typeof(int),
                    DataType = DbType.Int32,
                    Direction = ParameterDirection.ReturnValue
                };
            }
        }
    }

    private static void LoadCommandResults(SqlConnection connection, DatabaseSchema db,
        Dictionary<string, string> commandTypes, bool deepLoad)
    {
        foreach (var command in db.Commands)
        {
            // Skip scalar functions - they return a value, not a result set
            if (commandTypes.TryGetValue(command.FullName, out var objType) && objType == "FN")
                continue;

            // Table-valued functions (IF, TF) need SELECT * FROM syntax;
            // stored procedures use EXEC syntax
            var isTableValuedFunction = commandTypes.TryGetValue(command.FullName, out var tvfType)
                && (tvfType == "IF" || tvfType == "TF");

            // Strategy 1: CommandBehavior.SchemaOnly (fast, supports multiple result sets)
            try
            {
                LoadCommandResultsViaSchemaOnly(connection, command, isTableValuedFunction);
            }
            catch { /* SchemaOnly can fail for dynamic SQL, computed SELECTs, etc. */ }

            // Strategy 2: Transactional execution with ROLLBACK (handles temp tables,
            // dynamic SQL, and other edge cases by actually running the proc).
            // Only used when DeepLoad is enabled, as it actually executes procedures.
            if (deepLoad && command.CommandResults.Count == 0)
            {
                try
                {
                    LoadCommandResultsViaTransaction(connection, command, isTableValuedFunction);
                }
                catch { /* Transactional can also fail; CommandResults stays empty. */ }
            }
        }
    }

    private static void LoadCommandResultsViaSchemaOnly(SqlConnection connection,
        CommandSchema command, bool isTableValuedFunction)
    {
        using var cmd = new SqlCommand { Connection = connection };

        if (isTableValuedFunction)
        {
            var defaults = string.Join(", ", command.Parameters
                .Where(p => p.Direction != ParameterDirection.ReturnValue)
                .Select(_ => "DEFAULT"));
            cmd.CommandText = $"SELECT * FROM [{command.FullName.Replace(".", "].[")}]({defaults})";
        }
        else
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = command.FullName;
            foreach (var p in command.Parameters)
            {
                if (p.Direction == ParameterDirection.ReturnValue) continue;
                var sqlParam = new SqlParameter(p.Name, DBNull.Value);
                if (p.Direction == ParameterDirection.InputOutput)
                    sqlParam.Direction = ParameterDirection.InputOutput;
                cmd.Parameters.Add(sqlParam);
            }
        }

        using var reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly);
        ReadResultSetsFromReader(reader, command);
    }

    /// <summary>
    /// Fallback: actually executes the proc with NULL params inside a transaction that
    /// gets rolled back. This handles temp tables, dynamic SQL, and other edge cases
    /// that SchemaOnly cannot resolve. Matches the CodeSmith transactional strategy.
    /// </summary>
    private static void LoadCommandResultsViaTransaction(SqlConnection connection,
        CommandSchema command, bool isTableValuedFunction)
    {
        // Build: BEGIN TRANSACTION; EXEC [schema].[proc] NULL, DEFAULT, ...; ROLLBACK TRANSACTION
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BEGIN TRANSACTION");
        sb.AppendFormat("EXEC [{0}]", command.FullName.Replace(".", "].["));
        sb.AppendLine();

        var inputParams = command.Parameters
            .Where(p => p.Direction != ParameterDirection.ReturnValue)
            .ToList();
        for (int i = 0; i < inputParams.Count; i++)
        {
            var isTableType = inputParams[i].DataType == System.Data.DbType.Object;
            sb.Append(isTableType ? "\tDEFAULT" : "\tNULL");
            if (i < inputParams.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.AppendLine("ROLLBACK TRANSACTION");

        using var cmd = new SqlCommand(sb.ToString(), connection);
        using var reader = cmd.ExecuteReader();

        ReadResultSetsFromReader(reader, command);
    }

    private static void ReadResultSetsFromReader(SqlDataReader reader, CommandSchema command)
    {
        do
        {
            var schemaTable = reader.GetSchemaTable();
            if (schemaTable == null || schemaTable.Rows.Count == 0)
                continue;

            var result = new CommandResultSchema();
            int unnamedIndex = 0;
            foreach (DataRow row in schemaTable.Rows)
            {
                var columnName = row["ColumnName"]?.ToString();
                if (string.IsNullOrEmpty(columnName))
                    columnName = $"Column{++unnamedIndex}";

                var nativeType = row["DataTypeName"]?.ToString() ?? "";
                var baseType = nativeType.Split('(')[0].Trim();
                var resolved = ResolveType(baseType);

                var isNullable = row["AllowDBNull"] is true;
                var precision = row["NumericPrecision"] is short np ? (byte)np : (byte)0;
                var scale = row["NumericScale"] is short ns ? (byte)ns : (byte)0;
                var rawSize = row["ColumnSize"] is int cs ? (short)cs : (short)0;

                result.Columns.Add(new CommandResultColumnSchema
                {
                    Name = columnName,
                    FullName = $"{command.FullName}.{columnName}",
                    Database = command.Database,
                    NativeType = baseType,
                    SystemType = resolved.SystemType,
                    DataType = resolved.DbType,
                    AllowDBNull = isNullable,
                    Precision = precision,
                    Scale = scale,
                    Size = rawSize
                });
            }

            if (result.Columns.Count > 0)
                command.CommandResults.Add(result);
        } while (reader.NextResult());
    }

    // ── Utilities ────────────────────────────────────────────────────────────────

    private static (Type SystemType, DbType DbType) ResolveType(string nativeType)
    {
        return TypeMap.TryGetValue(nativeType, out var mapped)
            ? mapped
            : (typeof(object), DbType.Object);
    }

    private static ColumnSchema FindColumn(TableSchema table, string columnName)
    {
        foreach (var col in table.Columns)
            if (col.Name == columnName)
                return col;
        return null;
    }

    private static MemberColumnSchema CreateMemberColumn(ColumnSchema sourceColumn, string columnName)
    {
        var member = new MemberColumnSchema
        {
            Name = columnName,
            Column = sourceColumn
        };

        if (sourceColumn != null)
        {
            member.FullName = sourceColumn.FullName;
            member.Database = sourceColumn.Database;
            member.NativeType = sourceColumn.NativeType;
            member.SystemType = sourceColumn.SystemType;
            member.DataType = sourceColumn.DataType;
            member.Size = sourceColumn.Size;
            member.Precision = sourceColumn.Precision;
            member.Scale = sourceColumn.Scale;
            member.AllowDBNull = sourceColumn.AllowDBNull;
            member.IsUnique = sourceColumn.IsUnique;
        }

        return member;
    }
}
