using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;

namespace SchemaExplorer
{
    public class TableSchema : SchemaObjectBase
    {
        public Collection<ColumnSchema> Columns { get; } = new Collection<ColumnSchema>();
        public Collection<TableKeySchema> ForeignKeys { get; } = new Collection<TableKeySchema>();
        public Collection<TableKeySchema> PrimaryKeys { get; } = new Collection<TableKeySchema>();
        public Collection<IndexSchema> Indexes { get; } = new Collection<IndexSchema>();
        public bool HasPrimaryKey { get; set; }
        public PrimaryKeySchema PrimaryKey { get; set; }

        public IEnumerable<ColumnSchema> ForeignKeyColumns =>
            Columns.Where(c => c.IsForeignKeyMember);

        public DataTable GetTableData() => throw new System.NotImplementedException();
    }
}
