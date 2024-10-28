using Microsoft.CodeAnalysis;

using SourceCrafter.Helpers;

using System;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;

using static SourceCrafter.Helpers.Extensions;

namespace SourceCrafter.Mappify;

internal sealed class TypeMap
{
    static int id = 0;
    private readonly Action<StringBuilder, string, string> _mapper = null!, _reverseMapper = null!;
    private MappingKind _mappingKind;
    private readonly Mappers _mappers;
    private readonly TypeMeta _source, _target;
    internal readonly int Id;
    internal readonly bool IsValid = true;
    private readonly bool _ignoreTargetType, _ignoreSourceType;
    private readonly int _targetMemberCount, _sourceMemberCount;
    internal bool AddTargetTryGet, AddSourceTryGet;
    private readonly bool isTargetRecursive, isSourceRecursive, hasComplexSTTMembers;
    internal readonly TypeMap ItemMap;
    private bool _isScalar;

    private readonly string
        MethodName,
        ReverseMethodName;

    private readonly bool AreSameType;
    private readonly TypeMeta TargetType;
    private readonly TypeMeta SourceType;
    private readonly bool IsCollection;
    private readonly CollectionMapping CollectionMap, CollectionReverseMap;
    private readonly bool HasScalarConversion, HasReverseScalarConversion, HasMapping, HasReverseMapping;

    public TypeMap(
        Mappers mappers,
        ref TypeMap _this,
        int id,
        TypeMeta source,
        TypeMeta target,
        Applyment ignore,
        MemberContext sourceCtx = default,
        MemberContext targetCtx = default)
    {
        _this = this;

        var sameType = AreSameType = target.Id == source.Id;

        if (sameType)
        {
            MethodName = ReverseMethodName = "Copy";
        }
        else
        {
            MethodName = "To" + target.SanitizedName;
            ReverseMethodName = "To" + source.SanitizedName;
        }

        _mappers = mappers;

        Id = id;
        _source = source;
        _target = target;
        AreSameType = target.Id == source.Id;
        TargetType = target;
        SourceType = source;

        if (IsCollection = source.IsCollection || target.IsCollection)
        {
            ItemMap = mappers.GetOrAdd(source.Collection.ItemType, target.Collection.ItemType, ignore,
                source.Collection.IsItemNullable, target.Collection.IsItemNullable);

            if (!ItemMap.IsValid ||
                !(source.Collection.ItemType.HasZeroArgsCtor && target.Collection.ItemType.HasZeroArgsCtor))
            {
                IsValid = false;

                return;
            }

            ItemMap.TargetType.IsRecursive |= ItemMap.TargetType.IsRecursive;
            ItemMap.SourceType.IsRecursive |= ItemMap.SourceType.IsRecursive;

            CollectionMap = BuildCollectionMapping(source.Collection, target.Collection, MethodName);
            CollectionReverseMap = BuildCollectionMapping(target.Collection, source.Collection, ReverseMethodName);

            //Continue collection mappings here

            MemberMeta sourceItemMember = new(id, "sourceItem", true, source.Collection.IsItemNullable, ItemMap.SourceType, source, true, 0);
            MemberMeta targetItemMember = new(id, "targetItem", true, target.Collection.IsItemNullable, ItemMap.TargetType, target, true, 0);

            if (sourceItemMember.CantMap(true, targetItemMember, out sourceCtx, out targetCtx))
            {
                IsValid = false;
                return;
            }

            if (IsValid = !targetCtx.Ignore)
            {
                _mapper = (code, source, target) => code.Append(MethodName).Append('(').Append(source).Append(", ").Append(target).Append(')');
            };

            if (IsValid |= !sameType && !sourceCtx.Ignore)

                _reverseMapper = new CollectionMapper(ItemMap, targetItemMember, sourceItemMember, targetCtx, sourceCtx);

            return;
        }

        ItemMap = null!;

        if (SourceType.HasConversion(TargetType, out var targetScalarConversion, out var sourceScalarConversion))
        {
            MemberMeta sourceMember = new(id, "source", true, source.Collection.IsItemNullable, ItemMap.SourceType, source, true, 0);
            MemberMeta targetMember = new(id, "target", true, target.Collection.IsItemNullable, ItemMap.TargetType, target, true, 0);

            if (ignore < Applyment.Target && targetScalarConversion.exists)
            {
                //var scalar = targetScalarConversion.isExplicit
                //    ? $"({TargetType.FullName}){{0}}"
                //: "{0}";

                _mapper = targetScalarConversion.isExplicit 
                    ? (code, sourceItem) => code.Append('(').Append(TargetType.FullName).Append(')').Append(source)
                    : (code, sourceItem) => code.Append(source);

                IsValid = HasMapping = _isScalar = HasScalarConversion = true;
            }

            if (!sameType && ignore is not (Applyment.Source or Applyment.Both) && sourceScalarConversion.exists)
            {
                //var scalar = sourceScalarConversion.isExplicit
                //    ? $"({SourceType.FullName}){{0}}"
                //    : "{0}";

                _mapper = sourceScalarConversion.isExplicit
                    ? (code, sourceItem) => code.Append('(').Append(SourceType.FullName).Append(')').Append(source)
                    : (code, sourceItem) => code.Append(source);

                IsValid = HasReverseMapping = _isScalar = HasReverseScalarConversion = true;
            }
        }

        if (TargetType.IsPrimitive || SourceType.IsPrimitive || source.IsMemberless || target.IsMemberless)
        {
            return;
        }

        Action<StringBuilder, string?, string?>? _targetMembers = null, _sourceMembers = null;

        int memberCount = 0, reverseMemberCount = 0;

        var allowLowerCase = source.IsTupleType || target.IsTupleType /*, hasMatches = false*/;

        foreach (var targetMember in source.Members)
        {
            foreach (var sourceMember in target.Members)
            {
                var memberMappingId = GetId(sourceMember.Type.Id, sourceMember.Type.Id);

                if (Id == memberMappingId)
                {
                    if (targetMember.CantMap(allowLowerCase, sourceMember, out var sourceContext, out var targetContext))
                    {
                        if (targetMember == sourceMember) break;

                        continue;
                    }

                    if (!targetContext.Ignore)
                    {
                        memberCount++;
                        _targetMembers += _mapper.Build;
                    }

                    if (sourceContext.Ignore || sourceMember.Type.IsInterface || sameType) continue;

                    reverseMemberCount++;
                    _sourceMembers += _reverseMapper.Build;

                    // IsValid |= 
                    //     TryCreateMemberMap(
                    //         sourceMember,
                    //         targetMember,
                    //         sourceContext,
                    //         targetContext,
                    //         ref _targetMembers)
                    //     | (!sourceMember.Type.IsInterface && 
                    //        !sameType &&
                    //        TryCreateMemberMap(
                    //           targetMember,
                    //           sourceMember,
                    //           targetContext,
                    //           sourceContext,
                    //           ref _sourceBuilder));

                    // Determine assignment possibility
                    // Can be assigned

                    if (!IsValid) IsValid = true;
                }
                else if (mappers.GetOrAdd(targetMember, sourceMember, ignore) is { IsValid: true } found)
                {
                    if (targetMember.CantMap(allowLowerCase, sourceMember, out var sourceContext, out var targetContext))
                    {
                        if (targetMember == sourceMember) break;

                        continue;
                    }

                    if (!targetContext.Ignore)
                    {
                        memberCount++;
                        _targetMembers += found._mapper.Build;
                    }

                    if (sourceContext.Ignore || sourceMember.Type.IsInterface || sameType)
                        continue;

                    _sourceMembers += found._reverseMapper.Build;

                    //if (_mappingKind == MappingKind.All && found._mappingKind != MappingKind.All && (sourceMember.IsInitOnly || targetMember.IsInitOnly))
                    //{
                    //    found._mappingKind = MappingKind.All;
                    //}

                    //if (true == (IsValid |= HasTargetToSourceMap || HasSourceToTargetMap)
                    //    && (!found.TargetType.IsPrimitive || found.TargetType.IsTupleType
                    //    || !found.SourceType.IsPrimitive || found.SourceType.IsTupleType))
                    //{
                    //    AuxiliarMappings += found.BuildMethods;
                    //}
                }
            }


        }
    }

    //private bool TryCreateMemberMap(
    //    MemberMeta source,
    //    MemberMeta target,
    //    MemberContext sourceContext,
    //    MemberContext targetContext,
    //    ref Action<StringBuilder>? mapper)
    //{
    //    if (targetContext.Ignore || target is not { IsAccessible: true })
    //    {
    //        return false;
    //    }

    //    //if(target)

    //    return true;
    //}

    //sealed class CollectionMapper(TypeMap map,
    //    MemberMeta source,
    //    MemberMeta target,
    //    MemberContext sourceContext,
    //    MemberContext targetContext) : IMapper
    //{
    //    private string? comma;

    //    public void Build(StringBuilder obj, string? sourceItem = null, string? targetItem = null)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

    //sealed class StructuredObjectMapper(TypeMap map,
    //    MemberMeta source,
    //    MemberMeta target,
    //    MemberContext sourceContext) : IMapper
    //{

    //    private string? comma;

    //    public void Build(StringBuilder obj, string? sourceItem = null, string? targetItem = null)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}


    //sealed class TupleMapper(
    //    TypeMap map,
    //    MemberMeta sourceKey,
    //    MemberMeta sourceValue,
    //    MemberMeta targetKey,
    //    MemberMeta targetValue,
    //    MemberContext sourceContext,
    //    MemberContext targetContext) : IMapper
    //{
    //    private string? leftComma, rightComma;

    //    public void Build(StringBuilder obj, string? sourceItem = null, string? targetItem = null)
    //    {

    //    }
    //}

    //sealed class ValueBuilder()
    //{

    //}

    //sealed class ValueAssignment()
    //{

    //}

    //sealed class ScalarMapper(
    //    string targetTypeFullName,
    //    bool isExplicitCast
    //    ValueAssignment assign) : IMapper
    //{
    //    readonly string template = isExplicitCast
    //        ? $@"({targetTypeFullName}){{0}}"
    //        : "{0}";

    //    void IMapper.Build(StringBuilder code, string sourceItem)
    //    {
    //    }
    //}

    //// sealed class KeyValuePairMapper: IMapper
    //// {
    ////     internal KeyValuePairMapper(
    ////         TypeMap map,
    ////         MemberMeta sourceKey,
    ////         MemberMeta sourceValue,
    ////         MemberMeta targetKey,
    ////         MemberMeta targetValue,
    ////         MemberContext sourceContext,
    ////         MemberContext targetContext)
    ////     {
    ////     }
    //// }

    //sealed class KeyValuePairMapper(
    //        TypeMap map,
    //        MemberMeta sourceKey,
    //        MemberMeta sourceValue,
    //        MemberMeta targetKey,
    //        MemberMeta targetValue,
    //        MemberContext sourceContext,
    //        MemberContext targetContext) : IMapper
    //{
    //    public void Build(StringBuilder obj, string? sourceItem = null, string? targetItem = null)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}



    //internal interface IMapper
    //{
    //    void Build(StringBuilder obj, string? sourceItem = null, string? targetItem = null);
    //}

    //static void GenerateValue
    //(
    //    StringBuilder code,
    //    string item,
    //    Action<StringBuilder, string> generateValue,
    //    bool checkNull,
    //    bool call,
    //    bool isValueType,
    //    string? sourceBang,
    //    string? defaultSourceBang
    //)
    //{
    //    var indexerBracketPos = item.IndexOf('[');

    //    bool hasIndexer = indexerBracketPos > -1,
    //        shouldCache = checkNull && call && (hasIndexer || item.Contains('.'));

    //    var itemCache = shouldCache
    //        ? "_" + (hasIndexer ? item[..indexerBracketPos] : item).Replace(".", "")
    //        : item;

    //    if (checkNull)
    //    {
    //        if (call)
    //        {
    //            code.Append(item).Append(" is {} ").Append(itemCache).Append(" ? ");

    //            generateValue(code, itemCache);

    //            code.Append(" : default").Append(defaultSourceBang);
    //        }
    //        else
    //        {
    //            if (isValueType && defaultSourceBang != null)
    //            {
    //                var startIndex = code.Length;

    //                generateValue(code, itemCache);

    //                if (code[startIndex] == '(')
    //                {
    //                    var count = code.Length - startIndex;

    //                    code.Replace(item, "(" + item, startIndex, count)
    //                        .Insert(startIndex + count + 1, " ?? default")
    //                        .Append(defaultSourceBang).Append(")");
    //                }
    //                else
    //                {
    //                    code.Append(" ?? default").Append(defaultSourceBang);
    //                }
    //            }
    //            else
    //            {
    //                generateValue(code, item);

    //                code.Append(sourceBang);
    //            }
    //        }
    //    }
    //    else
    //    {
    //        generateValue(code, itemCache);

    //        code.Append(sourceBang);
    //    }
    //}

    private static CollectionMapping BuildCollectionMapping(CollectionMeta source, CollectionMeta target,
        string copyMethodName)
    {
        var isDictionary = source.IsDictionary && target.IsDictionary || CanMap(source, target) ||
                           CanMap(target, source);

        var iterator = !isDictionary && source.Indexable && target.BackingArray ? "for" : "foreach";

        return new(target.BackingArray,
            target.BackingArray && !source.Indexable,
            iterator,
            !source.Countable && target.BackingArray,
            target.Method,
            copyMethodName);

        static bool CanMap(CollectionMeta source, CollectionMeta target) =>
            source.IsDictionary && target.ItemType.Symbol is INamedTypeSymbol
            {
                IsTupleType: true, TupleElements.Length: 2
            };
    }

    internal static int GetId(int typeAId, int typeBId) =>
        (Math.Min(typeAId, typeAId), Math.Max(typeBId, typeBId)).GetHashCode();

    //public void DiscoverMappings()
    //{
    //    IsValid = IsCollectionMapping(typeMetaA, typeMetaB)
    //              || IsPrimitiveOrCastMapping(typeMetaA, typeMetaB)
    //              || HasMemberMappings(typeMetaA, typeMetaB);
    //}

    private bool IsEnumerableType(string fullNonGenericName, ITypeSymbol type, out CollectionMeta info)
    {
        if (type.IsPrimitive(true))
        {
            info = default!;
            return false;
        }

        switch (fullNonGenericName)
        {
            case "global::System.Collections.Generic.Dictionary" or "global::System.Collections.Generic.IDictionary"
                :
                info = GetCollectionInfo(EnumerableType.Dictionary, GetEnumerableType(type, true));

                return true;

            case "global::System.Collections.Generic.Stack"
                :
                info = GetCollectionInfo(EnumerableType.Stack, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.Queue"
                :
                info = GetCollectionInfo(EnumerableType.Queue, GetEnumerableType(type));

                return true;

            case "global::System.ReadOnlySpan"
                :

                info = GetCollectionInfo(EnumerableType.ReadOnlySpan, GetEnumerableType(type));

                return true;

            case "global::System.Span"
                :
                info = GetCollectionInfo(EnumerableType.Span, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
                :
                info = GetCollectionInfo(EnumerableType.Collection, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
                :
                info = GetCollectionInfo(EnumerableType.ReadOnlyCollection, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.IEnumerable"
                :
                info = GetCollectionInfo(EnumerableType.Enumerable, GetEnumerableType(type));

                return true;

            default:
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                {
                    info = GetCollectionInfo(EnumerableType.Array, elType);

                    return true;
                }
                else
                    foreach (var item in type.AllInterfaces)
                        if (IsEnumerableType(item.ToGlobalNonGenericNamespace(), item, out info))
                            return true;

                break;
        }

        info = default!;

        return false;
    }

    private static ITypeSymbol GetEnumerableType(ITypeSymbol enumerableType, bool isDictionary = false)
    {
        if (isDictionary)
            return ((INamedTypeSymbol)enumerableType)
                .AllInterfaces
                .First(i => i.Name.StartsWith("IEnumerable"))
                .TypeArguments
                .First();

        return ((INamedTypeSymbol)enumerableType)
            .TypeArguments
            .First();
    }

    private CollectionMeta GetCollectionInfo(EnumerableType enumerableType, ITypeSymbol typeSymbol)
    {
        var itemDataType = _mappers.Types.GetOrAdd(typeSymbol = typeSymbol.AsNonNullable(), null,
            enumerableType == EnumerableType.Dictionary);

        return enumerableType switch
        {
#pragma warning disable format
            EnumerableType.Dictionary =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.Queue =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    true,
                    false,
                    "Enqueue",
                    "Count"),
            EnumerableType.Stack =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    true,
                    false,
                    "Push",
                    "Count"),
            EnumerableType.Enumerable =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    false,
                    true,
                    null,
                    "Length"),
            EnumerableType.ReadOnlyCollection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.ReadOnlySpan =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    null,
                    "Length"),
            EnumerableType.Collection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.Span =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    null,
                    "Length"),
            _ =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    null,
                    "Length")
#pragma warning restore format
        };
    }
}

readonly record struct CollectionMeta(
    TypeMeta ItemType,
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

internal record struct CollectionMapping(
    bool CreateArray,
    bool UseLenInsteadOfIndex,
    string Iterator,
    bool Redim,
    string? Method,
    string MethodName);