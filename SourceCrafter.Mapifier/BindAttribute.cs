using System;
using SourceCrafter.Mapifier.Constants;


namespace SourceCrafter.Mapifier.Attributes
{
#pragma warning disable CS9113 // Parameter is unread.
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class BindAttribute<TIn, TOut>(MappingKind kind = MappingKind.All, IgnoreBind ignore = IgnoreBind.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class BindAttribute<TIn>(MappingKind kind = MappingKind.All, IgnoreBind ignore = IgnoreBind.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class BindAttribute(string memberNameof, IgnoreBind ignore = IgnoreBind.None) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class IgnoreBindAttribute(IgnoreBind ignore = IgnoreBind.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class MaxRecursionAttribute(short count = 1, IgnoreBind ignore = IgnoreBind.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = true)]
    public sealed class ExtendAttribute(string ignore = "") : Attribute;

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ExtendAttribute<T>(string ignore = "") : Attribute where T : Enum;    
#pragma warning restore CS9113 // Parameter is unread.
}

namespace SourceCrafter.Mapifier
{
    public interface IImplement<TInterface, TImplementation> where TImplementation : class, TInterface;
}