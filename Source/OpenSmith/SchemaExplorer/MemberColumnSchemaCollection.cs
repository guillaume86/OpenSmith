using System.Collections.ObjectModel;

namespace SchemaExplorer
{
    public class MemberColumnSchemaCollection : Collection<MemberColumnSchema>
    {
        public bool Contains(string name)
        {
            foreach (var item in this)
                if (item.Name == name)
                    return true;
            return false;
        }
    }
}
