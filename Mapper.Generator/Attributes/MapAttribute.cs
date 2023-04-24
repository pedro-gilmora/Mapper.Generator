using System;
using System.Collections.Generic;
using System.Text;

namespace Mapper.Generator.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public class MapAttribute<T> : Attribute
{
    public MapAttribute(string mapper = "") { }
}
