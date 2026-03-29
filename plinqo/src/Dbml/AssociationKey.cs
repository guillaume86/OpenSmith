using System;

namespace LinqToSqlShared.DbmlObjectModel;

[Serializable]
public class AssociationKey : IEquatable<AssociationKey>
{
    public AssociationKey(string name, bool isForeignKey)
    {
        Name = name;
        IsForeignKey = isForeignKey;
    }

    public bool IsForeignKey { get; }

    public string Name { get; }

    public override bool Equals(object obj)
    {
        if (obj is AssociationKey key)
            return Equals(key);

        return base.Equals(obj);
    }

    public override int GetHashCode()
    {
        string hash = Name + IsForeignKey.ToString();
        return hash.GetHashCode();
    }

    public bool Equals(AssociationKey other)
    {
        if (other == null)
            return false;

        return (Name == other.Name && IsForeignKey == other.IsForeignKey);
    }

    public static AssociationKey CreateForeignKey(string name) =>
        new AssociationKey(name, true);

    public static AssociationKey CreatePrimaryKey(string name) =>
        new AssociationKey(name, false);
}
