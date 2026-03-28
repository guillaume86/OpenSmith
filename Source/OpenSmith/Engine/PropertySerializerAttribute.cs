using System;

namespace OpenSmith.Engine
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public class PropertySerializerAttribute : Attribute
    {
        public PropertySerializerAttribute(Type serializerType) { }
    }
}
