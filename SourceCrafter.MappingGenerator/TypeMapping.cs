using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.MappingGenerator;
using System;
using System.Text;

namespace SourceCrafter.Bindings;

internal struct TypeMapping(uint id, int _next, TypeMappingInfo info)
{
    internal readonly uint
        Id = id;

    internal int next = _next;
    internal readonly TypeMappingInfo Info = info;
}

#pragma warning disable CS8618
internal class TypeMappingInfo(uint id, TypeData target, TypeData source, bool sameType = false)
#pragma warning restore CS8618
{
    internal ValueBuilder
        BuildToTargetValue = default!,
        BuildToSourceValue = default!;

    internal readonly string
        ToTargetMethodName = (sameType = target.Id == source.Id)
            ? "Copy"
            : $"To{target.SanitizedName}",
        ToSourceMethodName = sameType
            ? "Copy"
            : $"To{source.SanitizedName}",
        TryGetTargetMethodName = sameType
            ? "TryCopy"
            : $"TryGet{target.SanitizedName}",
        TryGetSourceMethodName = sameType
            ? "TryCopy"
            : $"TryGet{source.SanitizedName}",
        FillTargetFromSourceMethodName = sameType
            ? "Update"
            : $"FillFrom{source.SanitizedName}",
        FillSourceFromTargetMethodName = sameType
            ? "Update"
            : $"FillFrom{target.SanitizedName}";


    internal readonly uint
        Id = id;

    internal bool _rendered = false;

    internal Action<StringBuilder>? AuxiliarMappings;

    internal byte
        TargetMaxDepth = 2,
        SourceMaxDepth = 2;
    internal readonly bool
        AreSameType = target.Id == source.Id,
        IsScalar = target.IsPrimitive && source.IsPrimitive;

    internal readonly bool CanDepth = !target.IsPrimitive && !source.IsPrimitive
            && (target._typeSymbol.TypeKind, source._typeSymbol.TypeKind) is not (TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown, TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown),
        IsTupleFromClass = target.IsTupleType && source is { IsPrimitive: false, IsIterable: false },
        IsReverseTupleFromClass = source.IsTupleType && target is { IsPrimitive: false, IsIterable: false };


    internal bool? CanMap, IsCollection;

    internal readonly TypeData TargetType = target, SourceType = source;

    internal CollectionMapping TTSCollection, STTCollection;

    internal uint ItemMapId;

    internal bool
        AddTTSTryGet,
        AddSTTTryGet,
        HasToTargetScalarConversion,
        HasToSourceScalarConversion,
        JustFill,
        RequiresSTTCall,
        RequiresTTSCall,
        RenderedSTTDefaultMethod, RenderedSTTTryGetMethod, RenderedSTTFillMethod,
        RenderedTTSDefaultMethod, RenderedTTSTryGetMethod, RenderedTTSFillMethod;


    internal bool IsSTTRendered => RenderedSTTDefaultMethod && RenderedSTTTryGetMethod && RenderedSTTFillMethod;
    internal bool IsTTSRendered => RenderedTTSDefaultMethod && RenderedTTSTryGetMethod && RenderedTTSFillMethod;


    internal MappingKind MappingsKind { get; set; }

    internal int STTMemberCount, TTSMemberCount;

    internal Action<StringBuilder>? BuildToTargetMethod, BuildToSourceMethod;

    internal bool HasTargetToSourceMap => HasToSourceScalarConversion || IsCollection is true || TTSMemberCount > 0;
    internal bool HasSourceToTargetMap => HasToTargetScalarConversion || IsCollection is true || STTMemberCount > 0;

    internal void GatherTarget(ref TypeData? targetExistent, int targetId)
    {
        if (TargetType.Id == targetId)
            targetExistent = TargetType;
        else if (SourceType.Id == targetId)
            targetExistent = SourceType;
    }

    internal void GatherSource(ref TypeData? sourceExistent, int sourceId)
    {
        if (SourceType.Id == sourceId)
            sourceExistent = SourceType;
        else if (TargetType.Id == sourceId)
            sourceExistent = TargetType;
    }

    public override string ToString() => $"{TargetType.FullName} <=> {SourceType.FullName}";

    internal void EnsureDirection(ref Member target, ref Member source)
    {
        target.Type ??= TargetType;
        source.Type ??= SourceType;

        if ((TargetType.Id, SourceType.Id) == (source.Type.Id, target.Type.Id))
            (target, source) = (source, target);
    }

    internal void BuildMethods(Action<string, string> addSource)
    {

        StringBuilder code = new(@"#nullable enable
namespace SourceCrafter.Bindings;

public static partial class BindingExtensions
{");
        var len = code.Length;

        BuildMethods(code);

        if (code.Length == len)
            return;

        code.Append(@"
}");

        addSource($"Mappings.{Id}", code.ToString());
    }

    internal void BuildMethods(StringBuilder code)
    {
        if (CanMap is not true || IsScalar && (!TargetType.IsTupleType || !HasTargetToSourceMap) && (!SourceType.IsTupleType || !HasSourceToTargetMap) || _rendered) return;

        _rendered = true;
        if (HasSourceToTargetMap)
        {
            BuildToTargetMethod?.Invoke(code);
        }

        if (!AreSameType && HasTargetToSourceMap)
            BuildToSourceMethod?.Invoke(code);

        AuxiliarMappings?.Invoke(code);
    }
}


sealed record CollectionInfo(
    ITypeSymbol TypeSymbol,
    EnumerableType Type,
    bool IsItemNullable,
    bool Indexable,
    bool ReadOnly,
    bool Countable,
    bool BackingArray,
    string? Method,
    string CountProp)
{
    internal TypeData ItemDataType = null!, DataType = null!;
}

record struct CollectionMapping(bool CreateArray, bool UseLenInsteadOfIndex, string Iterator, bool Redim, string? Method, string ToTargetMethodName);