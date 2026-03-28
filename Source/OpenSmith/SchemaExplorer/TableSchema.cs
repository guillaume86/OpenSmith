using System.Collections.ObjectModel;
using System.Data;

namespace SchemaExplorer
{
    public class TableSchema : SchemaObjectBase
    {
        public Collection<ColumnSchema> Columns { get; } = new Collection<ColumnSchema>();
        public Collection<TableKeySchema> ForeignKeys { get; } = new Collection<TableKeySchema>();
        public Collection<TableKeySchema> PrimaryKeys { get; } = new Collection<TableKeySchema>();
        public bool HasPrimaryKey { get; set; }
        public PrimaryKeySchema PrimaryKey { get; set; }

        public DataTable GetTableData() => throw new System.NotImplementedException();
    }
}
