using System;
using System.Collections.ObjectModel;

namespace LinqToSqlShared.DbmlObjectModel;

[Serializable]
public class FunctionCollection : KeyedCollection<string, Function>
{
    protected override string GetKeyForItem(Function item) => item.Name;
}
