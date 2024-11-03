using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using System;
using System.Text;
using System.Runtime.CompilerServices;
using SourceCrafter.Bindings.Helpers;

namespace SourceCrafter.Bindings;

internal delegate void MethodRenderer(StringBuilder code, ref RenderFlags rendered);

internal struct TypeMappingEntry(uint id, int next, TypeMapping info)
{
    internal readonly uint
        Id = id;

    internal int Next = next;
    internal readonly TypeMapping Info = info;
}

#pragma warning disable CS8618
internal class TypeMapping
#pragma warning restore CS8618
{
    internal ValueBuilder?
        BuildTargetValue = default,
        BuildSourceValue = default;

    internal readonly string
        ToTargetMethodName,
        ToSourceMethodName,
        TryGetTargetMethodName,
        TryGetSourceMethodName,
        FillTargetMethodName,
        FillSourceMethodName;

    internal readonly int Id;

    private bool _rendered = false;

    internal Action<StringBuilder>? ExtraMappings;

    // internal byte
    //     TargetMaxDepth = 2,
    //     SourceMaxDepth = 2;

    private readonly bool
        _isScalar;

    internal readonly bool
        AreSameType,
        IsObjectMapping,
        CanDepth,
        IsTupleFromClass,
        IsReverseTupleFromClass;


    internal bool? CanMap;

    internal readonly TypeMeta TargetType, SourceType;

    internal CollectionMapping SourceCollectionMap, TargetCollectionMap;

    internal int ItemMapId;

    internal bool
        AddSourceTryGet,
        AddTargetTryGet,
        TargetHasScalarConversion,
        SourceHasScalarConversion,
        TargetRequiresMapper,
        SourceRequiresMapper,
        IsCollection;

    private RenderFlags _sourceRenderFlags, _targetRenderFlags;


    internal bool IsTargetRendered => _targetRenderFlags is not (false, false, false, false);
    internal bool IsSourceRendered => _sourceRenderFlags is not (false, false, false, false);

    internal int TargetMemberCount, SourceMemberCount;

    internal MethodRenderer? BuildTargetMethod, BuildSourceMethod;

    internal KeyValueMappings? TargetKeyValueMap, SourceKeyValueMap;

    internal MappingKind MappingsKind { get; set; }

    internal bool HasTargetToSourceMap => SourceHasScalarConversion || IsCollection is true || SourceMemberCount > 0;

    internal bool HasSourceToTargetMap => TargetHasScalarConversion || IsCollection is true || TargetMemberCount > 0;

    internal void CollectTarget(ref TypeMeta existingTarget, int targetId)
    {
        if (TargetType.Id == targetId)
            existingTarget = TargetType;
        else if (SourceType.Id == targetId)
            existingTarget = SourceType;
    }

    internal void CollectSource(ref TypeMeta existingSource, int sourceId)
    {
        if (SourceType.Id == sourceId)
            existingSource = SourceType;
        else if (TargetType.Id == sourceId)
            existingSource = TargetType;
    }

    public override string ToString() => $"{TargetType.FullName} <=> {SourceType.FullName}";

    internal void EnsureDirection(ref MemberMeta target, ref MemberMeta source)
    {
        target.Type ??= TargetType;
        source.Type ??= SourceType;

        if ((TargetType.Id, SourceType.Id) == (source.Type.Id, target.Type.Id))
            (target, source) = (source, target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal string GetFileName()
    {
        string source = SourceType.SanitizedName,
            target = TargetType.SanitizedName;

        ISymbol sourceContainer = SourceType.Type.ContainingType ?? (ISymbol)SourceType.Type.ContainingNamespace,
                targetContainer = TargetType.Type.ContainingType ?? (ISymbol)TargetType.Type.ContainingNamespace;

        while (source == target)
        {
            switch (sourceContainer, targetContainer)
            {
                case (not null or INamespaceSymbol { IsGlobalNamespace: true }, null):
                    return sourceContainer.ToNameOnly() + source + "_" + target;
                case (null, not null or INamespaceSymbol { IsGlobalNamespace: true }):
                    return source + "_" + targetContainer.ToNameOnly() + target;
                case (not null, not null):
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
        if (CanMap is not true || _isScalar && (!TargetType.IsTupleType || !HasTargetToSourceMap) && (!SourceType.IsTupleType || !HasSourceToTargetMap) || _rendered) return;

        _rendered = true;

        if (HasSourceToTargetMap)
        {
            BuildTargetMethod?.Invoke(code, ref _targetRenderFlags);
        }

        if (!AreSameType && HasTargetToSourceMap)
        {
            BuildSourceMethod?.Invoke(code, ref _sourceRenderFlags);
        }

        ExtraMappings?.Invoke(code);
    }

    public TypeMapping(int id, TypeMeta target, TypeMeta source)
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
            : "TryGet";
        TryGetSourceMethodName = sameType
            ? "TryCopy"
            : "TryGet";
        FillTargetMethodName = sameType
            ? "Update"
            : "Fill";
        FillSourceMethodName = sameType
            ? "Update"
            : "Fill";

        Id = id;
        AreSameType = target.Id == source.Id;
        _isScalar = target.IsPrimitive && source.IsPrimitive;
        CanDepth = !target.IsPrimitive && !source.IsPrimitive
            && (target.Type.TypeKind, source.Type.TypeKind) is not (TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown, TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown);
        IsTupleFromClass = target.IsTupleType && source is { IsPrimitive: false, IsIterable: false };
        IsReverseTupleFromClass = source.IsTupleType && target is { IsPrimitive: false, IsIterable: false };
        TargetType = target;
        SourceType = source;
        IsObjectMapping = source.IsObject || target.IsObject;

        if (IsCollection = SourceType.IsIterable && TargetType.IsIterable)
        {
            TargetCollectionMap = BuildCollectionMapping(SourceType.CollectionInfo, TargetType.CollectionInfo, ToTargetMethodName, FillTargetMethodName);
            SourceCollectionMap = BuildCollectionMapping(TargetType.CollectionInfo, SourceType.CollectionInfo, ToSourceMethodName, FillSourceMethodName);
        }
    }

    private static CollectionMapping BuildCollectionMapping(CollectionInfo source, CollectionInfo target, string copyMethodName, string fillMethodName)
    {
        var isDictionary = source.IsDictionary && target.IsDictionary || CanMap(source, target) || CanMap(target, source);

        var iterator = !isDictionary && source.Indexable && target.BackingArray ? "for" : "foreach";

        return new(
            isDictionary,
            target.BackingArray,
            target.BackingArray && !source.Indexable,
            iterator,
            !source.Countable && target.BackingArray,
            target.Method,
            copyMethodName,
            fillMethodName);

        static bool CanMap(CollectionInfo source, CollectionInfo target) => 
            source.IsDictionary && target.ItemDataType.Type is INamedTypeSymbol { IsTupleType: true, TupleElements.Length: 2 };
    }
}

internal readonly record struct CollectionInfo(
    TypeMeta ItemDataType,
    EnumerableType Type,
    bool IsItemNullable,
    bool Indexable,
    bool Countable,
    bool BackingArray,
    string? Method,
    string CountProp)
{
    internal readonly bool IsDictionary = Type is EnumerableType.Dictionary;
};

internal record struct CollectionMapping(bool IsDictionary, bool CreateArray, bool UseLenInsteadOfIndex, string Iterator, bool Redim, string? Method, string MethodName, string FillMethodName);

internal record KeyValueMappings(ValueBuilder Key, ValueBuilder Value);