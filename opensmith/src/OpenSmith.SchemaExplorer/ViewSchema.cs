using System.Collections.ObjectModel;

namespace SchemaExplorer
{
    public class ViewSchema : SchemaObjectBase
    {
        public Collection<ViewColumnSchema> Columns { get; } = new Collection<ViewColumnSchema>();
    }
}
