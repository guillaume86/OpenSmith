using System.ComponentModel;
using System.Xml.Serialization;
using OpenSmith.Engine;

namespace LinqToSqlShared.Generator;

[Serializable, PropertySerializer(typeof(XmlPropertySerializer))]
[TypeConverter(typeof(ExpandableObjectConverter))]
public class NamingProperty
{
    public NamingProperty() { }

    [NotifyParentProperty(true), Description("Table naming convention used in the database.")]
    public TableNamingEnum TableNaming { get; set; } = TableNamingEnum.Singular;

    [NotifyParentProperty(true), Description("Desired naming naming convention to be used by generator.")]
    public EntityNamingEnum EntityNaming { get; set; } = EntityNamingEnum.Singular;

    [NotifyParentProperty(true), Description("Desired association naming convention to be used by generator.")]
    public AssociationNamingEnum AssociationNaming { get; set; } = AssociationNamingEnum.ListSuffix;

    public override string ToString() => "(Expand to edit...)";
}
