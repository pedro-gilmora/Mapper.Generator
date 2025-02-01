using System;
using SourceCrafter.Mappify;


namespace SourceCrafter.Mappify.Attributes
{
#pragma warning disable CS9113 // Parameter is unread.
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class MapAttribute<TIn, TOut>(MappingKind kind = MappingKind.All, GenerateOn ignore = GenerateOn.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class MapAttribute<TIn>(MappingKind kind = MappingKind.All, GenerateOn ignore = GenerateOn.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class MapAttribute(string memberNameof, GenerateOn ignore = GenerateOn.None) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class IgnoreForAttribute(GenerateOn ignore = GenerateOn.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class IgnoreAttribute(GenerateOn ignore = GenerateOn.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class MaxAttribute(short count = 1, GenerateOn ignore = GenerateOn.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = true)]
    public sealed class ExtendAttribute(string ignore = "") : Attribute;

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ExtendAttribute<T>(string ignore = "") : Attribute where T : Enum;
    
#pragma warning restore CS9113 // Parameter is unread.
}

namespace SourceCrafter.Mappify
{
    public interface IImplement<IInterface, IImplementation>
        where IImplementation : class, IInterface;
}