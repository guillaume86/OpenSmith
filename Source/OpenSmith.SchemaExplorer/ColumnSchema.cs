namespace SchemaExplorer
{
    public class ColumnSchema : DataObjectBase, IColumnSchema
    {
        public bool IsPrimaryKeyMember { get; set; }
        public bool IsForeignKeyMember { get; set; }
        public bool IsUnique { get; set; }
        public TableSchema Table { get; set; }
    }
}
