using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Xml.Serialization;

namespace LinqToSqlShared.Generator.DbmlEnum;

[GeneratedCode("xsd", "2.0.50727.3038")]
[Serializable]
[DebuggerStepThrough]
[DesignerCategory("code")]
[XmlType(Namespace = "http://tempuri.org/DbmlEnum.xsd")]
[XmlRoot("Database", Namespace = "http://tempuri.org/DbmlEnum.xsd", IsNullable = false)]
public class Database
{
    private List<Enum> _enums = null;
    private string _name;

    public static Database DeserializeFromFile(string fileName)
    {
        Database db = null;

        if (File.Exists(fileName))
        {
            using var fileStream = new FileStream(fileName, FileMode.Open);
            var serializer = new XmlSerializer(typeof(Database));
            db = (Database)serializer.Deserialize(fileStream);
        }

        return db;
    }

    public void SerializeToFile(string fileName)
    {
        Sort();

        using var fileStream = new FileStream(fileName, FileMode.Create);
        var serializer = new XmlSerializer(typeof(Database));
        serializer.Serialize(fileStream, this);
    }

    public void Sort()
    {
        Enums = Enums.OrderBy(e => e.Name).ToList();

        foreach (var enumerator in Enums)
            enumerator.Sort();
    }

    [XmlElement("Enum")]
    public List<Enum> Enums
    {
        get => _enums ??= [];
        set => _enums = value;
    }

    [XmlAttribute]
    public string Name
    {
        get => _name;
        set => _name = value;
    }
}

[GeneratedCode("xsd", "2.0.50727.3038")]
[Serializable]
[DebuggerStepThrough]
[DesignerCategory("code")]
[XmlType(Namespace = "http://tempuri.org/DbmlEnum.xsd")]
public class Enum
{
    private List<Item> _items = null;
    private string _name;
    private string _table;
    private string _type;
    private AccessModifier _accessModifier = AccessModifier.Public;
    private bool _flags;
    private bool _includeDataContract = true;

    public void Sort()
    {
        Items = Items.OrderBy(v => v.Value).ToList();
    }

    [XmlElement("Item")]
    public List<Item> Items
    {
        get => _items ??= [];
        set => _items = value;
    }

    [XmlAttribute]
    public string Name
    {
        get => _name;
        set => _name = value;
    }

    [XmlAttribute]
    public string Table
    {
        get => _table;
        set => _table = value;
    }

    [XmlAttribute]
    public string Type
    {
        get => _type;
        set => _type = value;
    }

    [XmlAttribute]
    public AccessModifier AccessModifier
    {
        get => _accessModifier;
        set => _accessModifier = value;
    }

    [XmlAttribute]
    public bool Flags
    {
        get => _flags;
        set => _flags = value;
    }

    [XmlAttribute]
    public bool IncludeDataContract
    {
        get => _includeDataContract;
        set => _includeDataContract = value;
    }
}

[GeneratedCode("xsd", "2.0.50727.3038")]
[Serializable]
[DebuggerStepThrough]
[DesignerCategory("code")]
[XmlType(Namespace = "http://tempuri.org/DbmlEnum.xsd")]
public class Item
{
    private string _name;
    private long _value;
    private string _description;
    private bool _dataContractMember = true;

    [XmlAttribute]
    public string Name
    {
        get => _name;
        set => _name = value;
    }

    [XmlAttribute]
    public long Value
    {
        get => _value;
        set => _value = value;
    }

    [XmlAttribute]
    public string Description
    {
        get => _description;
        set => _description = value;
    }

    [XmlAttribute]
    public bool DataContractMember
    {
        get => _dataContractMember;
        set => _dataContractMember = value;
    }
}

[GeneratedCode("xsd", "2.0.50727.3038")]
[Serializable]
[XmlType(Namespace = "http://tempuri.org/DbmlEnum.xsd")]
public enum AccessModifier
{
    Public,
    Internal
}
