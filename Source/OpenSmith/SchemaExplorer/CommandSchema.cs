using System.Collections.ObjectModel;

namespace SchemaExplorer
{
    public class CommandSchema : SchemaObjectBase
    {
        public Collection<ParameterSchema> Parameters { get; } = new Collection<ParameterSchema>();
        public Collection<CommandResultSchema> CommandResults { get; } = new Collection<CommandResultSchema>();
        public ParameterSchema ReturnValueParameter { get; set; }
    }
}
