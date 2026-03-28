namespace SchemaExplorer
{
    public abstract class SchemaObjectBase
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public ExtendedPropertyCollection ExtendedProperties { get; } = new ExtendedPropertyCollection();
        public DatabaseSchema Database { get; set; }
    }
}
