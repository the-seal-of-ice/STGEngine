using System;

namespace STGEngine.Core.Serialization
{
    /// <summary>
    /// Marks a class with a YAML type tag for polymorphic serialization.
    /// TypeRegistry scans assemblies for this attribute at startup.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TypeTagAttribute : Attribute
    {
        public string Tag { get; }
        public TypeTagAttribute(string tag) => Tag = tag;
    }
}
