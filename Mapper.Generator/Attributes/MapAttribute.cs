using Mapper.Generator.Constants;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mapper.Generator.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public class MapAttribute<T> : Attribute
{
    public MapAttribute(string mapper = "") { }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class MapAttribute : Attribute
{
    public MapAttribute(string mapper = "", Ignore ignore = Ignore.None) { }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class IgnoreAttribute : Attribute
{
    public IgnoreAttribute(Ignore twoWay = Ignore.Both) { }
}
