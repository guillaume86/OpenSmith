namespace SchemaExplorer
{
    public class MemberColumnSchema : DataObjectBase, IColumnSchema
    {
        public bool IsPrimaryKeyMember { get; set; }
        public bool IsForeignKeyMember { get; set; }
        public bool IsUnique { get; set; }
        public ColumnSchema Column { get; set; }
    }
}
