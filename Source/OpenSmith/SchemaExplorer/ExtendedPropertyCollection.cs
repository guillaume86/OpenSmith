using System.Collections.ObjectModel;

namespace SchemaExplorer
{
    public class ExtendedPropertyCollection : KeyedCollection<string, ExtendedProperty>
    {
        protected override string GetKeyForItem(ExtendedProperty item) => item.Name;
    }
}
