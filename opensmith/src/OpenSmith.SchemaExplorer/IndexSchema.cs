namespace SchemaExplorer
{
    public class IndexSchema : SchemaObjectBase
    {
        public MemberColumnSchemaCollection MemberColumns { get; } = new MemberColumnSchemaCollection();
        public bool IsUnique { get; set; }
    }
}
