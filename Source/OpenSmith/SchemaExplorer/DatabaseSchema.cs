using System.Collections.ObjectModel;

namespace SchemaExplorer
{
    public class DatabaseSchema
    {
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        public DatabaseProvider Provider { get; set; }
        public Collection<TableSchema> Tables { get; } = new Collection<TableSchema>();
        public Collection<ViewSchema> Views { get; } = new Collection<ViewSchema>();
        public Collection<CommandSchema> Commands { get; } = new Collection<CommandSchema>();
    }
}
