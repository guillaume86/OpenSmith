using System;
using System.Collections.ObjectModel;

namespace LinqToSqlShared.DbmlObjectModel;

[Serializable]
public class TableFunctionParameterCollection : KeyedCollection<string, TableFunctionParameter>
{
    protected override string GetKeyForItem(TableFunctionParameter item) => item.ParameterName;
}
