using System;
using SourceCrafter.Bindings.Constants;


namespace SourceCrafter.Bindings.Attributes
{
#pragma warning disable CS9113 // Parameter is unread.
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class BindAttribute<TIn, TOut>(MappingKind kind = MappingKind.All, ApplyOn ignore = ApplyOn.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public class BindAttribute<TIn>(MappingKind kind = MappingKind.All, ApplyOn ignore = ApplyOn.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class BindAttribute(string memberNameof, ApplyOn ignore = ApplyOn.None) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class IgnoreBindAttribute(ApplyOn ignore = ApplyOn.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class MaxAttribute(short count = 1, ApplyOn ignore = ApplyOn.Both) : Attribute;
#pragma warning restore CS9113 // Parameter is unread.
}