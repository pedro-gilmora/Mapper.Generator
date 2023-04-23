using System;

namespace Mapper.Generator.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class MapWithAttribute : Attribute 
{
    public MapWithAttribute(string with) { }
}
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class MapFromAttribute : Attribute 
{
    public MapFromAttribute(string from) { }
}