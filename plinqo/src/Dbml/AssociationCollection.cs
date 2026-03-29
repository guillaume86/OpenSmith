using System;
using System.Collections.ObjectModel;

namespace LinqToSqlShared.DbmlObjectModel;

[Serializable]
public class AssociationCollection : KeyedCollection<AssociationKey, Association>, IProcessed
{
    protected override AssociationKey GetKeyForItem(Association item) => item.ToKey();

    public bool IsProcessed { get; set; }
}
