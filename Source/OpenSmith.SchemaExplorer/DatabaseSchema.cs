using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SchemaExplorer
{
    public class DatabaseSchema
    {
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        public DatabaseProvider Provider { get; set; }

        /// <summary>
        /// Legacy CodeSmith API: returns self for compatibility with SourceDatabase.Database.Name pattern.
        /// </summary>
        public DatabaseSchema Database => this;
        public SchemaObjectCollection<TableSchema> Tables { get; } = new();
        public Collection<ViewSchema> Views { get; } = new Collection<ViewSchema>();
        public Collection<CommandSchema> Commands { get; } = new Collection<CommandSchema>();
    }

    /// <summary>
    /// A collection that supports lookup by name (FullName or schema.name).
    /// </summary>
    public class SchemaObjectCollection<T> : Collection<T> where T : SchemaObjectBase
    {
        public T this[string name]
        {
            get => Items.FirstOrDefault(i =>
                string.Equals(i.FullName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public T this[string owner, string name]
        {
            get
            {
                var fullName = owner + "." + name;
                return this[fullName];
            }
        }
    }
}
