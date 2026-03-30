using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Data.SqlClient;
using OpenSmith.Engine;
using SchemaExplorer;
using LinqToSqlShared.DbmlObjectModel;
using Type = LinqToSqlShared.DbmlObjectModel.Type;
using ParameterDirection = LinqToSqlShared.DbmlObjectModel.ParameterDirection;

namespace LinqToSqlShared.Generator;

public class DbmlGenerator
{
    public event EventHandler<SchemaItemProcessedEventArgs> SchemaItemProcessed;

    public static string GetEnumXmlFileName(string mappingFile)
    {
        string dbmlExtension = Path.GetExtension(mappingFile);
        return mappingFile.Replace(dbmlExtension, ".enum.xml");
    }

    private static readonly Regex CleanIdRegex = new(
        @"(_ID|_id|_Id|\.ID|\.id|\.Id|ID|Id)$", RegexOptions.Compiled);

    private static readonly Regex CleanNumberPrefix = new(@"^\d+");

    // must be sorted
    private static readonly string[] ExistingContextProperties =
    [
        "ChangeConflicts", "CommandTimeout", "Connection",
        "DeferredLoadingEnabled",
        "LoadOptions", "Log",
        "Manager", "Mapping",
        "ObjectTrackingEnabled",
        "Provider",
        "RuleManager",
        "Services",
        "Transaction"
    ];

    private static readonly string[] FunctionProperties =
    [
        "CS_IsScalarFunction",
        "CS_IsTableValuedFunction",
        "CS_IsInlineTableValuedFunction",
        "CS_IsMultiStatementTableValuedFunction"
    ];

    private readonly GeneratorSettings settings;

    public GeneratorSettings Settings => settings;
    internal List<string> ClassNames { get; } = [];
    internal List<string> FunctionNames { get; } = [];
    internal List<string> AssociationNames { get; } = [];
    internal Dictionary<string, List<string>> PropertyNames { get; } = new();

    private Database _database;
    public Database Database => _database;

    private DbmlEnum.Database _enumDatabase;
    public DbmlEnum.Database EnumDatabase => _enumDatabase;

    private DbmlEnum.Database _existingEnumDatabase;
    public DbmlEnum.Database ExistingEnumDatabase => _existingEnumDatabase;

    public string EnumXmlFileName => GetEnumXmlFileName(settings.MappingFile);

    public DbmlGenerator(GeneratorSettings settings)
    {
        this.settings = settings;
    }

    public Database Create(DatabaseSchema databaseSchema)
    {
        _database = File.Exists(settings.MappingFile)
            ? Dbml.CopyWithFilledInDefaults(Dbml.FromFile(settings.MappingFile))
            : new Database();

        Database.Name = databaseSchema.Name;
        CreateContext(databaseSchema);

        _enumDatabase = new DbmlEnum.Database { Name = databaseSchema.Name };
        _existingEnumDatabase = DbmlEnum.Database.DeserializeFromFile(EnumXmlFileName) ?? new DbmlEnum.Database();

        foreach (TableSchema t in databaseSchema.Tables)
        {
            if (Settings.IsIgnored(t.FullName))
            {
                Debug.WriteLine($"Skipping Table: {t.FullName}");
            }
            else if (Settings.IsEnum(t))
            {
                Debug.WriteLine($"Getting Enum Table: {t.FullName}");
                GetEnum(t);
            }
            else
            {
                Debug.WriteLine($"Getting Table Schema: {t.FullName}");
                GetTable(t);
            }
            OnSchemaItemProcessed(t.FullName);
        }

        if (Settings.IncludeViews)
        {
            foreach (ViewSchema v in databaseSchema.Views)
            {
                if (Settings.IsIgnored(v.FullName))
                {
                    Debug.WriteLine($"Skipping View: {v.FullName}");
                }
                else
                {
                    Debug.WriteLine($"Getting View Schema: {v.FullName}");
                    CreateView(v);
                }
                OnSchemaItemProcessed(v.FullName);
            }
        }

        if (Settings.IncludeFunctions)
        {
            foreach (CommandSchema c in databaseSchema.Commands)
            {
                if (Settings.IsIgnored(c.FullName))
                {
                    Debug.WriteLine($"Skipping Function: {c.FullName}");
                }
                else
                {
                    Debug.WriteLine($"Getting Function Schema: {c.FullName}");
                    CreateFunction(c);
                }
                OnSchemaItemProcessed(c.FullName);
            }
        }

        //sync tables
        RemoveExtraMembers(_database);

        _database.Tables.Sort();
        Dbml.ToFile(Dbml.CopyWithNulledOutDefaults(_database),
            settings.MappingFile);

        if (_enumDatabase.Enums.Count > 0 || File.Exists(EnumXmlFileName))
            _enumDatabase.SerializeToFile(EnumXmlFileName);

        return _database;
    }

    private void CreateContext(DatabaseSchema databaseSchema)
    {
        if (!string.IsNullOrEmpty(Settings.ContextNamespace))
            Database.ContextNamespace = Settings.ContextNamespace;

        if (!string.IsNullOrEmpty(Settings.EntityNamespace))
            Database.EntityNamespace = Settings.EntityNamespace;

        if (!string.IsNullOrEmpty(Settings.EntityBase))
            Database.EntityBase = Settings.EntityBase;

        if (string.IsNullOrEmpty(Database.Class))
            Database.Class = string.IsNullOrEmpty(Settings.DataContextName)
                ? $"{StringUtil.ToPascalCase(databaseSchema.Name)}DataContext"
                : Settings.DataContextName;

        Database.Connection ??= new Connection("System.Data.SqlClient");

        if (Database.Connection.Mode == ConnectionMode.ConnectionString
            && string.IsNullOrEmpty(Database.Connection.ConnectionString))
            Database.Connection.Mode = ConnectionMode.AppSettings;

        if (!Database.Connection.Mode.HasValue)
            Database.Connection.Mode = ConnectionMode.ConnectionString;

        if (string.IsNullOrEmpty(Database.Connection.SettingsObjectName))
            Database.Connection.SettingsObjectName = "Properties.Settings";

        if (string.IsNullOrEmpty(Database.Connection.SettingsPropertyName))
            Database.Connection.SettingsPropertyName = $"{ToLegalName(databaseSchema.Name)}ConnectionString";

        if (string.IsNullOrEmpty(Database.Connection.ConnectionString))
            Database.Connection.ConnectionString = databaseSchema.ConnectionString;
    }

    private DbmlEnum.Enum GetEnum(TableSchema tableSchema)
    {
        // Anything coming in here should already have passed through Settings.IsEnum().
        // Because of this, we know not only that it is an Enum table, but also that all
        // of the desired columns exist.

        var myEnum = _enumDatabase.Enums.FirstOrDefault(e => e.Table == tableSchema.FullName);
        var existingEnum = _existingEnumDatabase.Enums.FirstOrDefault(e => e.Table == tableSchema.FullName)
            ?? new DbmlEnum.Enum();

        if (myEnum == null)
        {
            myEnum = new DbmlEnum.Enum
            {
                Name = string.IsNullOrEmpty(existingEnum.Name)
                    ? ToEnumName(tableSchema.Name)
                    : existingEnum.Name,
                Table = tableSchema.FullName,
                Type = tableSchema.PrimaryKey.MemberColumns[0].SystemType.FullName,
                Flags = existingEnum.Flags,
                IncludeDataContract = existingEnum.IncludeDataContract,
                Items = GetEnumItems(tableSchema, existingEnum)
            };
            _enumDatabase.Enums.Add(myEnum);
        }

        return myEnum;
    }

    private List<DbmlEnum.Item> GetEnumItems(TableSchema tableSchema, DbmlEnum.Enum existingEnum)
    {
        List<DbmlEnum.Item> itemList = [];

        string primaryKey = tableSchema.PrimaryKey.MemberColumns[0].Name;
        string nameColumn = settings.GetEnumNameColumnName(tableSchema);
        string descriptionColumn = settings.GetEnumDescriptionColumnName(tableSchema);

        DataTable table = tableSchema.GetTableData();
        if (table.Rows.Count == 0)
            throw new ApplicationException(
                $"Table '{tableSchema.FullName}' was identified as an enum table but does not contain any rows. Please insert rows into the table or ignore the table.");

        foreach (DataRow row in table.Rows)
        {
            long value = long.Parse(row[primaryKey].ToString());
            var existingValue = existingEnum.Items.FirstOrDefault(v => v.Value == value)
                ?? new DbmlEnum.Item();

            string description = table.Columns.Contains(descriptionColumn)
                ? row[descriptionColumn] as string
                : null;

            itemList.Add(new DbmlEnum.Item
            {
                Name = StringUtil.ToPascalCase(row[nameColumn].ToString()),
                Value = value,
                Description = description ?? existingValue.Description,
                DataContractMember = existingValue.DataContractMember
            });
        }

        return itemList;
    }

    private Table GetTable(TableSchema tableSchema) =>
        GetTable(tableSchema, true);

    private Table GetTable(TableSchema tableSchema, bool processAssociations)
    {
        Table t;
        string key = tableSchema.FullName;

        if (Database.Tables.Contains(key))
        {
            t = Database.Tables[key];

            // add sub type class names
            ClassNames.Add(t.Type.Name);
        }
        else
        {
            t = CreateTable(tableSchema);
        }

        if (!PropertyNames.ContainsKey(t.Type.Name))
            PropertyNames.Add(t.Type.Name, []);

        if (!t.Type.Columns.IsProcessed)
            CreateColumns(t, tableSchema);

        if (processAssociations && !t.Type.Associations.IsProcessed)
            CreateAssociations(t, tableSchema);

        t.Type.IsProcessed = true;
        t.IsProcessed = true;
        return t;
    }

    private Table CreateTable(TableSchema tableSchema)
    {
        var type = new Type(ToClassName(tableSchema.Name));
        var t = new Table(tableSchema.FullName, type);
        t.Member = t.Type.Name;

        if (Array.BinarySearch(ExistingContextProperties, t.Type.Name) >= 1)
            t.Member += "Table";

        Database.Tables.Add(t);

        return t;
    }

    private void CreateAssociations(Table table, TableSchema tableSchema)
    {
        foreach (TableKeySchema tableKey in tableSchema.ForeignKeys)
        {
            if (Settings.IsIgnored(tableKey.ForeignKeyTable.FullName)
                || Settings.IsIgnored(tableKey.PrimaryKeyTable.FullName)
                || Settings.IsEnum(tableKey.ForeignKeyTable)
                || Settings.IsEnum(tableKey.PrimaryKeyTable))
                continue;

            CreateAssociation(table, tableKey);
        }

        table.Type.Associations.IsProcessed = true;
    }

    private void CreateAssociation(Table foreignTable, TableKeySchema tableKeySchema)
    {
        if (Settings.IsIgnored(tableKeySchema.PrimaryKeyTable.FullName))
        {
            Debug.WriteLine($"Skipping Association because one of the tables is skipped: {tableKeySchema.Name}");
            Trace.WriteLine($"Skipping Association because one of the tables is skipped: {tableKeySchema.Name}");
            return;
        }

        // skip unsupported type
        if (tableKeySchema.ForeignKeyMemberColumns.Any(c => Settings.IsUnsupportedDbType(c))
            || tableKeySchema.PrimaryKeyMemberColumns.Any(c => Settings.IsUnsupportedDbType(c)))
        {
            Debug.WriteLine($"Skipping Association because one of the associated columns is an unsupported db type: {tableKeySchema.Name}");
            Trace.WriteLine($"Skipping Association because one of the associated columns is an unsupported db type: {tableKeySchema.Name}");
            return;
        }

        var primaryTable = GetTable(tableKeySchema.PrimaryKeyTable, false);

        string primaryClass = primaryTable.Type.Name;
        string foreignClass = foreignTable.Type.Name;

        string name = tableKeySchema.Name;
        name = MakeUnique(AssociationNames, name);

        string foreignMembers = GetKeyMembers(foreignTable,
            tableKeySchema.ForeignKeyMemberColumns, tableKeySchema.Name);

        string primaryMembers = GetKeyMembers(primaryTable,
            tableKeySchema.PrimaryKeyMemberColumns, tableKeySchema.Name);

        var key = AssociationKey.CreateForeignKey(name);
        bool isNew = !foreignTable.Type.Associations.Contains(key);

        Association foreignAssociation;

        if (isNew)
            foreignAssociation = new Association(name);
        else
            foreignAssociation = foreignTable.Type.Associations[key];

        foreignAssociation.IsForeignKey = true;
        foreignAssociation.ThisKey = foreignMembers;
        foreignAssociation.OtherKey = primaryMembers;
        foreignAssociation.Type = primaryClass;

        string prefix = GetMemberPrefix(foreignAssociation, primaryClass, foreignClass);

        if (isNew)
        {
            foreignAssociation.Member = ToPropertyName(foreignTable.Type.Name, prefix + primaryClass);
            foreignAssociation.Storage = CommonUtility.GetFieldName(foreignAssociation.Member);
            foreignTable.Type.Associations.Add(foreignAssociation);
        }
        else
        {
            PropertyNames[foreignTable.Type.Name].Add(foreignAssociation.Member);
        }

        // add reverse association
        if (!Settings.IsReverseAssociationExcluded(name))
        {
            key = AssociationKey.CreatePrimaryKey(name);
            isNew = !primaryTable.Type.Associations.Contains(key);

            Association primaryAssociation;
            if (isNew)
                primaryAssociation = new Association(name);
            else
                primaryAssociation = primaryTable.Type.Associations[key];

            primaryAssociation.IsForeignKey = false;
            primaryAssociation.ThisKey = foreignAssociation.OtherKey;
            primaryAssociation.OtherKey = foreignAssociation.ThisKey;
            primaryAssociation.Type = foreignClass;

            bool isOneToOne = IsOneToOne(tableKeySchema, foreignAssociation);

            if (primaryAssociation.Cardinality == null && isOneToOne)
                primaryAssociation.Cardinality = Cardinality.One;

            if (isNew)
            {
                string propertyName = prefix + foreignClass;
                if (!isOneToOne)
                {
                    if (settings.AssociationNaming == AssociationNamingEnum.ListSuffix)
                        propertyName += "List";
                    else if (settings.AssociationNaming == AssociationNamingEnum.Plural)
                        propertyName = StringUtil.ToPlural(propertyName);
                }

                primaryAssociation.Member = ToPropertyName(primaryTable.Type.Name, propertyName);
                primaryAssociation.Storage = CommonUtility.GetFieldName(primaryAssociation.Member);
                primaryTable.Type.Associations.Add(primaryAssociation);
            }
            else
            {
                PropertyNames[primaryTable.Type.Name].Add(primaryAssociation.Member);
            }

            primaryAssociation.IsProcessed = true;
        }

        if (IsTableKeySchemaCascadeDelete(tableKeySchema))
        {
            foreignAssociation.DeleteRule = "CASCADE";
        }

        if (settings.IncludeDeleteOnNull || foreignTable.Type.IsManyToMany())
        {
            if (!foreignAssociation.DeleteOnNull.HasValue && IsTableDeleteOnNull(tableKeySchema))
                foreignAssociation.DeleteOnNull = true;
        }
        else
        {
            foreignAssociation.DeleteOnNull = null;
        }

        foreignAssociation.IsProcessed = true;
    }

    private static bool IsOneToOne(TableKeySchema tableKeySchema, Association foreignAssociation)
    {
        bool isFkeyPkey = tableKeySchema.ForeignKeyTable.HasPrimaryKey
                          && tableKeySchema.ForeignKeyTable.PrimaryKey != null
                          && tableKeySchema.ForeignKeyTable.PrimaryKey.MemberColumns.Count == 1
                          && tableKeySchema.ForeignKeyTable.PrimaryKey.MemberColumns.Contains(foreignAssociation.ThisKey);

        if (isFkeyPkey)
            return true;

        // if f.key is unique
        foreach (var column in tableKeySchema.ForeignKeyMemberColumns)
            if (!column.IsUnique)
                return false;

        return true;
    }

    private static bool IsTableKeySchemaCascadeDelete(TableKeySchema tableKeySchema) =>
        tableKeySchema.ExtendedProperties.Contains("CS_CascadeDelete")
        && tableKeySchema.ExtendedProperties["CS_CascadeDelete"].Value is bool value
        && value;

    /// <summary>
    /// DeleteOnNull deletes an entity when a Associated Property on that entity is
    /// set to null and the Foreign Key for that association does not allow nulls.
    ///
    /// Should return true when all of the following conditions are met...
    /// A) A Foreign Key column on the entity does not allow null values.
    /// B) All dependancies on that entity will do a cascade delete.
    /// </summary>
    /// <param name="tableKeySchema">TableKeySchema.ForeignKeyTable is the table being analyzed.</param>
    /// <returns>Value of AssociationAttribute DeleteOnNull Property</returns>
    private static bool IsTableDeleteOnNull(TableKeySchema tableKeySchema)
    {
        bool foreignTableForeignKeyColumnAllowDBNull = tableKeySchema.ForeignKeyMemberColumns.Count == 0;
        foreach (MemberColumnSchema foreignColumn in tableKeySchema.ForeignKeyMemberColumns)
        {
            if (foreignColumn.AllowDBNull)
            {
                foreignTableForeignKeyColumnAllowDBNull = true;
                break;
            }
        }

        if (foreignTableForeignKeyColumnAllowDBNull)
            return false;

        bool foreignTablePrimaryKeysCascadeDelete = true;
        foreach (TableKeySchema foreignTableKeySchema in tableKeySchema.ForeignKeyTable.PrimaryKeys)
        {
            if (IsTableKeySchemaCascadeDelete(foreignTableKeySchema))
            {
                foreignTablePrimaryKeysCascadeDelete = false;
                break;
            }
        }

        return foreignTablePrimaryKeysCascadeDelete;
    }

    private static string GetKeyMembers(Table table, MemberColumnSchemaCollection members, string name)
    {
        StringBuilder keyMembers = new();

        foreach (MemberColumnSchema member in members)
        {
            if (!table.Type.Columns.Contains(member.Name))
                throw new InvalidOperationException(
                    $"Could not find column {member.Name} for assoication {name}.");

            var column = table.Type.Columns[member.Name];
            if (keyMembers.Length > 0)
                keyMembers.Append(',');

            keyMembers.Append(column.Member);
        }

        return keyMembers.ToString();
    }

    private void CreateView(ViewSchema viewSchema)
    {
        Table table;

        if (Database.Tables.Contains(viewSchema.FullName))
        {
            table = Database.Tables[viewSchema.FullName];
        }
        else
        {
            var type = new Type(ToClassName(viewSchema.Name));
            table = new Table(viewSchema.FullName, type);
            Database.Tables.Add(table);
        }

        if (string.IsNullOrEmpty(table.Type.Name))
            table.Type.Name = ToClassName(viewSchema.Name);

        if (string.IsNullOrEmpty(table.Member))
            table.Member = table.Type.Name;

        foreach (ViewColumnSchema columnSchema in viewSchema.Columns)
        {
            Column column;

            if (table.Type.Columns.Contains(columnSchema.Name))
            {
                column = table.Type.Columns[columnSchema.Name];
            }
            else
            {
                column = new Column(GetSystemType(columnSchema)) { Name = columnSchema.Name };
                table.Type.Columns.Add(column);
            }

            PopulateColumn(column, columnSchema, table.Type.Name);
        }

        table.Type.Columns.IsProcessed = true;
        table.Type.Associations.IsProcessed = true;
        table.IsProcessed = true;
    }

    private void CreateFunction(CommandSchema commandSchema)
    {
        Function function;
        string key = commandSchema.FullName;
        bool isNew = !Database.Functions.Contains(key);

        if (isNew)
        {
            function = new Function(key);
            Database.Functions.Add(function);
        }
        else
        {
            function = Database.Functions[key];
        }

        //Function/@Method is safe to update
        if (string.IsNullOrEmpty(function.Method))
        {
            string methodName = ToLegalName(commandSchema.Name);
            if (ClassNames.Contains(methodName))
                methodName += "Procedure";

            function.Method = MakeUnique(FunctionNames, methodName);
        }

        function.IsComposable = IsFunction(commandSchema);

        ParameterSchema returnParameter = null;

        function.Parameters.Clear();
        foreach (ParameterSchema p in commandSchema.Parameters)
        {
            if (p.Direction == System.Data.ParameterDirection.ReturnValue)
                returnParameter = p;
            else
                CreateParameter(function, p);
        }

        try
        {
            for (int i = 0; i < commandSchema.CommandResults.Count; i++)
            {
                var r = commandSchema.CommandResults[i];
                string defaultName = function.Method + "Result";
                if (commandSchema.CommandResults.Count > 1)
                    defaultName += (i + 1).ToString();

                CreateResult(function, r, defaultName, i);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading result schema: {ex.Message}");
        }

        function.HasMultipleResults = function.Types.Count > 1;
        function.IsProcessed = true;

        if (function.Types.Count != 0)
            return;

        function.Return ??= new Return("System.Int32");

        if (commandSchema.ReturnValueParameter != null)
        {
            function.Return.Type = GetSystemType(commandSchema.ReturnValueParameter);
            function.Return.DbType = GetDbType(commandSchema.ReturnValueParameter);
        }
        else if (returnParameter != null)
        {
            function.Return.Type = GetSystemType(returnParameter);
            function.Return.DbType = GetDbType(returnParameter);
        }
        else
        {
            function.Return.Type = "System.Int32";
            function.Return.DbType = "int";
        }
    }

    private void CreateResult(Function function, CommandResultSchema commandResultSchema, string defaultName, int index)
    {
        string name = defaultName;
        Type result;

        if (function.Types.Contains(name))
        {
            result = function.Types[name];
        }
        else if (function.Types.Count >= index + 1)
        {
            result = function.Types[index];
        }
        else
        {
            result = new Type(ToClassName(name));
            function.Types.Add(result);
        }

        if (result.IsProcessed)
            return;

        // ElementType/@Name safe attribute
        if (string.IsNullOrEmpty(result.Name))
            result.Name = ToClassName(name);

        foreach (CommandResultColumnSchema c in commandResultSchema.Columns)
        {
            Column column;

            if (result.Columns.Contains(c.Name))
            {
                column = result.Columns[c.Name];
            }
            else
            {
                column = new Column(GetSystemType(c)) { Name = c.Name };
                result.Columns.Add(column);
            }

            if (!column.IsProcessed)
                PopulateColumn(column, c, result.Name);
        }

        result.IsProcessed = true;
    }

    private void CreateParameter(Function function, ParameterSchema parameterSchema)
    {
        Parameter parameter;
        if (function.Parameters.Contains(parameterSchema.Name))
        {
            parameter = function.Parameters[parameterSchema.Name];
        }
        else
        {
            parameter = new Parameter(parameterSchema.Name, GetSystemType(parameterSchema));
            function.Parameters.Add(parameter);
        }

        //Parameter/@Parameter is safe
        if (string.IsNullOrEmpty(parameter.ParameterName))
        {
            string parameterName = parameterSchema.Name;
            if (parameterName.StartsWith('@'))
                parameterName = parameterName[1..];

            parameter.ParameterName = ToParameterName(parameterName);
        }

        parameter.Type = GetSystemType(parameterSchema);
        parameter.DbType = GetDbType(parameterSchema);

        switch (parameterSchema.Direction)
        {
            case System.Data.ParameterDirection.Input:
                parameter.Direction = ParameterDirection.In;
                break;
            case System.Data.ParameterDirection.InputOutput:
                parameter.Direction = ParameterDirection.InOut;
                break;
            case System.Data.ParameterDirection.Output:
                parameter.Direction = ParameterDirection.Out;
                break;
        }
    }

    private static string GetMemberPrefix(Association association, string primaryClass, string foreignClass)
    {
        bool isSameName = association.ThisKey.Equals(association.OtherKey,
            StringComparison.OrdinalIgnoreCase);
        isSameName = isSameName || association.ThisKey.Equals(
            primaryClass + association.OtherKey,
            StringComparison.OrdinalIgnoreCase);

        string prefix = string.Empty;
        if (isSameName)
            return prefix;

        prefix = association.ThisKey.Replace(association.OtherKey, "");
        prefix = prefix.Replace(",", "");
        prefix = prefix.Replace(primaryClass, "");
        prefix = prefix.Replace(foreignClass, "");
        prefix = CleanIdRegex.Replace(prefix, "");

        // FIX ME
        Regex regex = new(@"^\d");
        if (regex.IsMatch(prefix))
            prefix = $"My{prefix}";

        return prefix;
    }

    private string GetColumnType(DataObjectBase columnSchema)
    {
        return IsEnumAssociation(columnSchema as ColumnSchema, out string typeName)
            ? typeName
            : GetSystemType(columnSchema);
    }

    private bool IsOrWasEnumAssociation(DataObjectBase dataObject)
    {
        IsOrWasEnumAssociation(dataObject, out bool isEnum, out bool wasEnum);
        return isEnum || wasEnum;
    }

    private string IsOrWasEnumAssociation(DataObjectBase dataObject, out bool isEnum, out bool wasEnum)
    {
        string name = string.Empty;
        isEnum = false;
        wasEnum = false;
        
        // !HACK: Workaround for StackOverflowException
        return name;

        // if (dataObject is not ColumnSchema columnSchema || !columnSchema.IsForeignKeyMember)
        //     return name;

        // foreach (TableKeySchema tableKeySchema in columnSchema.Table.ForeignKeys)
        // {
        //     // find columns ...
        //     ColumnSchema primaryColumn = null;
        //     for (int i = 0; i < tableKeySchema.ForeignKeyMemberColumns.Count; i++)
        //     {
        //         if (tableKeySchema.ForeignKeyMemberColumns[i].Column != columnSchema)
        //             continue;

        //         primaryColumn = tableKeySchema.PrimaryKeyMemberColumns[i].Column;
        //         break;
        //     }

        //     if (primaryColumn == null)
        //         continue;

        //     // Is Enum
        //     var primaryKeyTable = tableKeySchema.PrimaryKeyTable;

        //     if (Settings.IsEnum(primaryKeyTable))
        //     {
        //         name = GetEnum(primaryKeyTable).Name;
        //         isEnum = true;
        //     }

        //     // Was Enum
        //     var existingEnum = _existingEnumDatabase.Enums
        //         .FirstOrDefault(e => e.Table == primaryKeyTable.FullName);

        //     if (existingEnum != null)
        //     {
        //         if (string.IsNullOrEmpty(name))
        //             name = existingEnum.Name;
        //         wasEnum = true;
        //     }

        //     if (isEnum || wasEnum)
        //         break;

        //     // find column in pktable, if that is fkey too, check that parent.
        //     if (primaryColumn.IsForeignKeyMember && primaryColumn != dataObject)
        //         return IsOrWasEnumAssociation(primaryColumn, out isEnum, out wasEnum);
        // }

        // return name;
    }

    private bool IsEnumAssociation(DataObjectBase columnSchema, out string typeName)
    {
        typeName = IsOrWasEnumAssociation(columnSchema, out bool isEnum, out _);
        return isEnum;
    }

    private void CreateColumns(Table table, TableSchema tableSchema)
    {
        foreach (ColumnSchema columnSchema in tableSchema.Columns)
        {
            // skip columns matching the ignore pattern (e.g., Cm\d{10} custom columns)
            if (Settings.IsColumnIgnored(columnSchema.Name))
            {
                Debug.WriteLine($"Skipping column '{columnSchema.Name}' because it matches the ignore pattern.");
                continue;
            }

            // skip unsupported type
            if (Settings.IsUnsupportedDbType(columnSchema))
            {
                Debug.WriteLine($"Skipping column '{columnSchema.Name}' because it has an unsupported db type '{columnSchema.NativeType}'.");
                Trace.WriteLine($"Skipping column '{columnSchema.Name}' because it has an unsupported db type '{columnSchema.NativeType}'.");
                continue;
            }

            bool isNew = !table.Type.TryFindColumn(columnSchema.Name, true, out Column column);

            if (isNew)
            {
                column = new Column(GetColumnType(columnSchema)) { Name = columnSchema.Name };
                table.Type.Columns.Add(column);
            }

            PopulateColumn(column, columnSchema, table.Type.Name);

            if (column.IsPrimaryKey != true)
                column.IsPrimaryKey = columnSchema.IsPrimaryKeyMember;
        }

        table.Type.Columns.IsProcessed = true;
    }

    private void PopulateColumn(Column column, DataObjectBase columnSchema, string className)
    {
        bool canUpdateType = string.IsNullOrEmpty(column.Type)
            || column.Type.StartsWith("System.")
            || IsOrWasEnumAssociation(columnSchema);

        if (canUpdateType)
            column.Type = GetColumnType(columnSchema);

        if (!PropertyNames.ContainsKey(className))
            PropertyNames.Add(className, []);

        //Column/@Member is edit safe if it is NullOrEmpty.
        // However, if the column starts with a digit, then we ARE going to update it.
        if (string.IsNullOrEmpty(column.Member) || CleanNumberPrefix.IsMatch(column.Member))
        {
            column.Member = ToPropertyName(className, columnSchema.Name);
            column.Storage = CommonUtility.GetFieldName(column.Member);
        }
        else
        {
            PropertyNames[className].Add(column.Member);
        }

        if (columnSchema.NativeType.Equals("text", StringComparison.OrdinalIgnoreCase)
            || columnSchema.NativeType.Equals("ntext", StringComparison.OrdinalIgnoreCase)
            || columnSchema.NativeType.Equals("xml", StringComparison.OrdinalIgnoreCase)
            || columnSchema.NativeType.Equals("binary", StringComparison.OrdinalIgnoreCase)
            || columnSchema.NativeType.Equals("image", StringComparison.OrdinalIgnoreCase))
            column.UpdateCheck = UpdateCheck.Never;

        column.DbType = GetDbType(columnSchema);
        column.CanBeNull = columnSchema.AllowDBNull;
        column.IsVersion = IsRowVersion(columnSchema);

        if (!column.IsDbGenerated.HasValue)
            column.IsDbGenerated = IsDbGenerated(columnSchema);

        column.IsProcessed = true;
    }

    private static string GetSystemType(DataObjectBase d)
    {
        if (d.SystemType == typeof(XmlDocument))
            return "System.Xml.Linq.XElement";

        if (d.SystemType == typeof(byte[]))
            return "System.Data.Linq.Binary";

        return d.SystemType.ToString();
    }

    private static string MakeUnique(ICollection<string> existingItems, string name)
    {
        string uniqueName = name;
        int count = 1;

        while (existingItems.Contains(uniqueName))
            uniqueName = string.Concat(name, count++);

        existingItems.Add(uniqueName);
        return uniqueName;
    }

    private void RemoveExtraMembers(Database database)
    {
        List<Table> removedTables = [];
        List<Column> removedColumns = [];
        List<Association> removedAssociations = [];
        List<Function> removedFunctions = [];
        List<Type> removedFunctionTypes = [];
        List<Column> removedFunctionColumns = [];

        foreach (var table in database.Tables)
        {
            if (!table.IsProcessed)
            {
                removedTables.Add(table);
                continue;
            }

            removedColumns.Clear();
            foreach (var c in table.Type.Columns)
                if (!c.IsProcessed)
                    removedColumns.Add(c);

            removedAssociations.Clear();
            foreach (var a in table.Type.Associations)
                if (!a.IsProcessed && !Settings.UserDefinedAssociations.Contains(a.Name))
                    removedAssociations.Add(a);

            foreach (var c in removedColumns)
                table.Type.Columns.Remove(c);

            foreach (var a in removedAssociations)
                table.Type.Associations.Remove(a);
        }

        foreach (var t in removedTables)
            database.Tables.Remove(t);

        foreach (var f in database.Functions)
        {
            if (!f.IsProcessed)
            {
                removedFunctions.Add(f);
                continue;
            }

            foreach (var t in f.Types)
            {
                if (!t.IsProcessed)
                {
                    removedFunctionTypes.Add(t);
                    continue;
                }

                foreach (var c in t.Columns)
                    if (!c.IsProcessed)
                        removedFunctionColumns.Add(c);

                foreach (var c in removedFunctionColumns)
                    t.Columns.Remove(c);
            }

            foreach (var t in removedFunctionTypes)
                f.Types.Remove(t);
        }

        foreach (var f in removedFunctions)
            database.Functions.Remove(f);
    }

    private string ToParameterName(string name)
    {
        string legalName = ToLegalName(name);
        legalName = CommonUtility.GetParameterName(legalName);
        return legalName;
    }

    private string ToEnumName(string name) =>
        ToClassName(name);

    private string ToClassName(string name)
    {
        if (settings.TableNaming != TableNamingEnum.Plural && settings.EntityNaming == EntityNamingEnum.Plural)
            name = StringUtil.ToPlural(name);
        else if (settings.TableNaming != TableNamingEnum.Singular && settings.EntityNaming == EntityNamingEnum.Singular)
            name = StringUtil.ToSingular(name);

        string legalName = ToLegalName(name);
        legalName = MakeUnique(ClassNames, legalName);
        return legalName;
    }

    private string ToPropertyName(string className, string name)
    {
        string propertyName = ToLegalName(name);
        if (className.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            propertyName += "Member";

        if (!PropertyNames.ContainsKey(className))
            PropertyNames.Add(className, []);

        propertyName = MakeUnique(PropertyNames[className], propertyName);
        return propertyName;
    }

    private string ToLegalName(string name)
    {
        string legalName = Settings.CleanName(name);
        string cleanName = CleanNumberPrefix.Replace(legalName, string.Empty, 1).Trim();

        // If a column's name is 123, then this will ensure that the name is valid by making the name Member123.
        if (string.IsNullOrEmpty(cleanName))
            legalName = $"Member{legalName}";
        else if (cleanName == ".")
            legalName = "Period";
        else if (cleanName == "'")
            legalName = "Apostrophe";
        else if (cleanName == "_")
            legalName = "Underscore";
        else
            legalName = cleanName;

        legalName = StringUtil.ToPascalCase(legalName);
        return legalName;
    }

    protected void OnSchemaItemProcessed(string name)
    {
        SchemaItemProcessed?.Invoke(this, new SchemaItemProcessedEventArgs(name));
    }

    private static bool IsFunction(SchemaObjectBase command)
    {
        foreach (var name in FunctionProperties)
        {
            if (!command.ExtendedProperties.Contains(name))
                continue;

            string temp = command.ExtendedProperties[name].Value.ToString();
            bool.TryParse(temp, out bool isFunction);

            if (isFunction)
                return true;
        }

        return false;
    }

    private static bool IsRowVersion(DataObjectBase column) =>
        column.NativeType.Equals("timestamp", StringComparison.OrdinalIgnoreCase)
        || column.NativeType.Equals("rowversion", StringComparison.OrdinalIgnoreCase);

    private static bool IsDbGenerated(DataObjectBase column)
    {
        if (IsRowVersion(column))
            return true;

        if (IsIdentity(column))
            return true;

        bool isComputed = false;
        string value;
        try
        {
            if (column.ExtendedProperties.Contains("CS_IsComputed"))
            {
                value = column.ExtendedProperties["CS_IsComputed"].Value.ToString();
                bool.TryParse(value, out isComputed);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error: {ex.Message}");
        }

        return isComputed;
    }

    private static bool IsIdentity(DataObjectBase column)
    {
        string temp;
        bool isIdentity = false;
        try
        {
            if (column.ExtendedProperties.Contains("CS_IsIdentity"))
            {
                temp = column.ExtendedProperties["CS_IsIdentity"].Value.ToString();
                bool.TryParse(temp, out isIdentity);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error: {ex.Message}");
        }

        return isIdentity;
    }

    private static string GetDbType(ParameterSchema parameterSchema)
    {
        if (parameterSchema.Database.Provider.Name != "SqlSchemaProvider")
            return parameterSchema.NativeType;

        // Table-valued parameters (user-defined table types) resolve to DbType.Object;
        // CodeSmith emits an empty DbType for these.
        if (parameterSchema.DataType == System.Data.DbType.Object)
            return "";

        var param = new SqlParameter { DbType = parameterSchema.DataType };

        return GetSqlTypeDeclaration(
            parameterSchema.NativeType,
            parameterSchema.Size,
            parameterSchema.Precision,
            parameterSchema.Scale,
            false,
            false);
    }

    private static string GetDbType(DataObjectBase column)
    {
        if (column.Database.Provider.Name != "SqlSchemaProvider")
            return column.NativeType;

        return GetSqlTypeDeclaration(
            column.NativeType,
            column.Size,
            column.Precision,
            column.Scale,
            !column.AllowDBNull,
            IsIdentity(column));
    }

    private static string GetSqlTypeDeclaration(string nativeType, int size, int precision, int scale, bool nonNull, bool isAutoIncrement)
    {
        StringBuilder builder = new();
        builder.Append(nativeType);

        switch (nativeType.Trim().ToLower())
        {
            case "binary":
            case "char":
            case "nchar":
            case "nvarchar":
            case "varbinary":
            case "varchar":
                // add size
                builder.AppendFormat("({0})", (size == 0x7fffffff || size == -1) ? "MAX" : size.ToString());
                break;
            case "decimal":
            case "numeric":
                // add scale
                builder.AppendFormat("({0},{1})", precision, scale);
                break;
        }

        if (nonNull)
            builder.Append(" NOT NULL");

        if (isAutoIncrement)
            builder.Append(" IDENTITY");

        return builder.ToString();
    }
}
