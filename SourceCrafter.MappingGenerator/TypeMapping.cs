using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings;
using System;
using System.Text;
using System.Security.Cryptography;
using System.Linq;
using System.Runtime.CompilerServices;
using SourceCrafter.Bindings.Helpers;

namespace SourceCrafter.Bindings;

internal struct TypeMapping(uint id, int _next, TypeMappingInfo info)
{
    internal readonly uint
        Id = id;

    internal int next = _next;
    internal readonly TypeMappingInfo Info = info;
}

#pragma warning disable CS8618
internal class TypeMappingInfo
#pragma warning restore CS8618
{
    internal ValueBuilder
        BuildToTargetValue = default!,
        BuildToSourceValue = default!;

    internal readonly string
        ToTargetMethodName,
        ToSourceMethodName,
        TryGetTargetMethodName,
        TryGetSourceMethodName,
        FillTargetMethodName,
        FillSourceMethodName;

    internal readonly uint Id;

    bool _rendered = false;

    internal Action<StringBuilder>? AuxiliarMappings;

    internal byte
        TargetMaxDepth = 2,
        SourceMaxDepth = 2;

    internal readonly bool
        AreSameType,
        IsScalar;

    internal readonly bool CanDepth,
        IsTupleFromClass,
        IsReverseTupleFromClass;


    internal bool? CanMap;

    internal readonly TypeData TargetType, SourceType;

    internal CollectionMapping TTSCollectionMapping, STTCollectionMapping;

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
        RenderedTTSDefaultMethod, RenderedTTSTryGetMethod, RenderedTTSFillMethod,
        IsCollection;


    internal bool IsSTTRendered => RenderedSTTDefaultMethod && RenderedSTTTryGetMethod && RenderedSTTFillMethod;
    internal bool IsTTSRendered => RenderedTTSDefaultMethod && RenderedTTSTryGetMethod && RenderedTTSFillMethod;


    internal MappingKind MappingsKind { get; set; }

    internal int STTMemberCount, TTSMemberCount;

    internal Action<StringBuilder>? BuildToTargetMethod, BuildToSourceMethod;
    internal KeyValueMappings? STTKeyValueMapping, TTSKeyValueMapping;

    internal bool HasTargetToSourceMap => HasToSourceScalarConversion || IsCollection is true || TTSMemberCount > 0;

    internal bool HasSourceToTargetMap => HasToTargetScalarConversion || IsCollection is true || STTMemberCount > 0;

    internal void CollectTarget(ref TypeData existingTarget, int targetId)
    {
        if (TargetType.Id == targetId)
            existingTarget = TargetType;
        else if (SourceType.Id == targetId)
            existingTarget = SourceType;
    }

    internal void CollectSource(ref TypeData existingSource, int sourceId)
    {
        if (SourceType.Id == sourceId)
            existingSource = SourceType;
        else if (TargetType.Id == sourceId)
            existingSource = TargetType;
    }

    public override string ToString() => $"{TargetType.FullName} <=> {SourceType.FullName}";

    internal void EnsureDirection(ref Member target, ref Member source)
    {
        target.Type ??= TargetType;
        source.Type ??= SourceType;

        if ((TargetType.Id, SourceType.Id) == (source.Type.Id, target.Type.Id))
            (target, source) = (source, target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal string GetMappingHash()
    {
        string source = SourceType.SanitizedName,
            target = TargetType.SanitizedName;

        ISymbol sourceContainer = SourceType._type.ContainingType ?? (ISymbol)SourceType._type.ContainingNamespace,
            targetContainer = TargetType._type.ContainingType ?? (ISymbol)TargetType._type.ContainingNamespace;

        while (source == target)
        {
            switch ((sourceContainer, targetContainer))
            {
                case (not null or INamespaceSymbol { IsGlobalNamespace: true }, null):
                    return sourceContainer.ToNameOnly() + source + "_" + target;
                case (null, not null or INamespaceSymbol { IsGlobalNamespace: true }):
                    return source + "_" + targetContainer.ToNameOnly() + target;
                case ({ }, { }):
                    source = sourceContainer.ToNameOnly() + source;
                    target = targetContainer.ToNameOnly() + target;
                    sourceContainer = sourceContainer.ContainingType ?? (ISymbol)sourceContainer.ContainingNamespace;
                    targetContainer = targetContainer.ContainingType ?? (ISymbol)targetContainer.ContainingNamespace;
                    continue;
                default:
                    return source;

            }
        }
        return source + "_" + target;
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
        {
            BuildToSourceMethod?.Invoke(code);
        }

        AuxiliarMappings?.Invoke(code);
    }

    public TypeMappingInfo(uint id, TypeData target, TypeData source)
    {
        var sameType = target.Id == source.Id;

        ToTargetMethodName = (sameType)
            ? "Copy"
            : $"To{target.SanitizedName}";
        ToSourceMethodName = sameType
            ? "Copy"
            : $"To{source.SanitizedName}";
        TryGetTargetMethodName = sameType
            ? "TryCopy"
            : $"TryGet{target.SanitizedName}";
        TryGetSourceMethodName = sameType
            ? "TryCopy"
            : $"TryGet{source.SanitizedName}";
        FillTargetMethodName = sameType
            ? "Update"
            : $"FillFrom{source.SanitizedName}";
        FillSourceMethodName = sameType
            ? "Update"
            : $"FillFrom{target.SanitizedName}";

        Id = id;
        AreSameType = target.Id == source.Id;
        IsScalar = target.IsPrimitive && source.IsPrimitive;
        CanDepth = !target.IsPrimitive && !source.IsPrimitive
            && (target._type.TypeKind, source._type.TypeKind) is not (TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown, TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown);
        IsTupleFromClass = target.IsTupleType && source is { IsPrimitive: false, IsIterable: false };
        IsReverseTupleFromClass = source.IsTupleType && target is { IsPrimitive: false, IsIterable: false };
        TargetType = target;
        SourceType = source;

        if (IsCollection = SourceType.IsIterable is true && TargetType.IsIterable is true)
        {
            STTCollectionMapping = BuildCollectionMapping(SourceType.CollectionInfo, TargetType.CollectionInfo, ToSourceMethodName, FillSourceMethodName);
            TTSCollectionMapping = BuildCollectionMapping(TargetType.CollectionInfo, SourceType.CollectionInfo, ToTargetMethodName, FillTargetMethodName);
        }
    }

    static CollectionMapping BuildCollectionMapping(CollectionInfo a, CollectionInfo b, string toMethodName, string fillMethodName)
    {
        bool redim = !a.Countable && b.BackingArray,
            isDictionary = a.IsDictionary && b.IsDictionary || CanMap(a, b) || CanMap(b, a);

        var iterator = !isDictionary && a.Indexable && b.BackingArray ? "for" : "foreach";

        return new(isDictionary, b.BackingArray, b.BackingArray && !a.Indexable, iterator, redim, b.Method, toMethodName, fillMethodName);

        static bool CanMap(CollectionInfo a, CollectionInfo b)
        {
            return a.IsDictionary && b.ItemDataType._type is INamedTypeSymbol { IsTupleType: true, TupleElements.Length: 2 };
        }
    }
}


readonly record struct CollectionInfo(
    TypeData ItemDataType,
    EnumerableType Type,
    bool IsItemNullable,
    bool Indexable,
    bool ReadOnly,
    bool Countable,
    bool BackingArray,
    string? Method,
    string CountProp)
{
    internal readonly bool IsDictionary = Type is EnumerableType.Dictionary;
};

record struct CollectionMapping(bool IsDictionary, bool CreateArray, bool UseLenInsteadOfIndex, string Iterator, bool Redim, string? Method, string MethodName, string FillMethodName);

internal record KeyValueMappings(ValueBuilder Key, ValueBuilder Value);