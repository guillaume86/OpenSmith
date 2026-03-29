using System.Text.RegularExpressions;
using SchemaExplorer;

namespace LinqToSqlShared.Generator;

public class GeneratorSettings
{
    public string MappingFile { get; set; }
    public string ContextNamespace { get; set; }
    public string DataContextName { get; set; }
    public string EntityBase { get; set; }
    public string EntityNamespace { get; set; }
    public bool IncludeViews { get; set; }
    public bool IncludeFunctions { get; set; }
    public List<Regex> IgnoreExpressions { get; } = [];
    public List<Regex> IncludeExpressions { get; } = [];
    public List<Regex> CleanExpressions { get; } = [];
    public List<Regex> EnumExpressions { get; } = [];
    public List<Regex> EnumNameExpressions { get; } = [];
    public List<Regex> EnumDescriptionExpressions { get; } = [];
    public List<string> UserDefinedAssociations { get; set; } = [];
    public bool DisableRenaming { get; set; }

    public bool IsIgnored(string name) =>
        !IsRegexMatch(name, IncludeExpressions) || IsRegexMatch(name, IgnoreExpressions);

    public bool IsColumnIgnored(string columnName) =>
        IsRegexMatch(columnName, IgnoreExpressions);

    public bool IsUnsupportedDbType(IColumnSchema column) =>
        column.NativeType.Equals("geography", StringComparison.OrdinalIgnoreCase)
        || column.NativeType.Equals("geometry", StringComparison.OrdinalIgnoreCase)
        || column.NativeType.Equals("hierarchyid", StringComparison.OrdinalIgnoreCase);

    public bool IsEnum(TableSchema table) =>
        table.HasPrimaryKey
        && table.PrimaryKey.MemberColumns.Count == 1
        && !IsIgnored(table.FullName)
        && IsRegexMatch(table.Name, EnumExpressions)
        && IsEnumSystemType(table.PrimaryKey.MemberColumns[0])
        && !string.IsNullOrEmpty(GetEnumNameColumnName(table));

    private static bool IsEnumSystemType(MemberColumnSchema column) =>
        column.NativeType.Equals("int", StringComparison.OrdinalIgnoreCase)
        || column.NativeType.Equals("bigint", StringComparison.OrdinalIgnoreCase)
        || column.NativeType.Equals("tinyint", StringComparison.OrdinalIgnoreCase)
        || column.NativeType.Equals("byte", StringComparison.OrdinalIgnoreCase)
        || column.NativeType.Equals("smallint", StringComparison.OrdinalIgnoreCase);

    public string GetEnumNameColumnName(TableSchema table)
    {
        string result = GetEnumColumnName(table, EnumNameExpressions);

        // If no Regex match found, use first column of type string.
        if (string.IsNullOrEmpty(result))
            foreach (ColumnSchema column in table.Columns)
                if (column.SystemType == typeof(string))
                {
                    result = column.Name;
                    break;
                }

        return result;
    }

    public string GetEnumDescriptionColumnName(TableSchema table) =>
        GetEnumColumnName(table, EnumDescriptionExpressions);

    private static string GetEnumColumnName(TableSchema table, List<Regex> regexList)
    {
        string result = string.Empty;

        foreach (ColumnSchema column in table.Columns)
            if (IsRegexMatch(column.Name, regexList))
            {
                result = column.Name;
                break;
            }

        return result;
    }

    private static bool IsRegexMatch(string name, List<Regex> regexList)
    {
        foreach (var regex in regexList)
            if (regex.IsMatch(name))
                return true;

        return false;
    }

    public string CleanName(string name)
    {
        if (CleanExpressions.Count == 0)
            return name;

        foreach (var regex in CleanExpressions)
        {
            if (regex.IsMatch(name))
                return regex.Replace(name, "");
        }

        return name;
    }

    public FrameworkEnum Framework { get; set; } = FrameworkEnum.v45;
    public TableNamingEnum TableNaming { get; set; } = TableNamingEnum.Singular;
    public EntityNamingEnum EntityNaming { get; set; } = EntityNamingEnum.Singular;
    public AssociationNamingEnum AssociationNaming { get; set; } = AssociationNamingEnum.ListSuffix;
    public bool IncludeDeleteOnNull { get; set; } = true;
    public bool IncludeDataContract { get; set; } = true;
    public bool GenerateMetaData { get; set; } = true;
}
