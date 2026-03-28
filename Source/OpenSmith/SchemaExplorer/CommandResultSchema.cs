using System.Collections.ObjectModel;

namespace SchemaExplorer
{
    public class CommandResultSchema
    {
        public Collection<CommandResultColumnSchema> Columns { get; } = new Collection<CommandResultColumnSchema>();
    }
}
