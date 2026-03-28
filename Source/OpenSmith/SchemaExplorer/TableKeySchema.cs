using System.Collections.ObjectModel;

namespace SchemaExplorer
{
    public class TableKeySchema : SchemaObjectBase
    {
        public TableSchema ForeignKeyTable { get; set; }
        public TableSchema PrimaryKeyTable { get; set; }
        public MemberColumnSchemaCollection ForeignKeyMemberColumns { get; } = new MemberColumnSchemaCollection();
        public MemberColumnSchemaCollection PrimaryKeyMemberColumns { get; } = new MemberColumnSchemaCollection();
    }
}
