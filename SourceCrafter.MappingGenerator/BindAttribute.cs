using System;
using SourceCrafter.Bindings.Constants;


namespace SourceCrafter.Bindings.Attributes
{
#pragma warning disable CS9113 // Parameter is unread.
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class BindAttribute<TIn, TOut>(MappingKind kind = MappingKind.All, ApplyOn ignore = ApplyOn.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class BindAttribute<TIn>(MappingKind kind = MappingKind.All, ApplyOn ignore = ApplyOn.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class BindAttribute(string memberNameof, ApplyOn ignore = ApplyOn.None) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class IgnoreBindAttribute(ApplyOn ignore = ApplyOn.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class MaxAttribute(short count = 1, ApplyOn ignore = ApplyOn.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = true)]
    public sealed class ExtendAttribute(string ignore = "") : Attribute;

    public interface IImplement<IInterface, IImplementation>
        where IImplementation : class, IInterface; 
    
#pragma warning restore CS9113 // Parameter is unread.
}