using System;

namespace SchemaExplorer
{
    public abstract class DataObjectBase : SchemaObjectBase
    {
        public Type SystemType { get; set; }
        public string NativeType { get; set; }
        public System.Data.DbType DataType { get; set; }
        public bool AllowDBNull { get; set; }
        public int Size { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }
    }
}
