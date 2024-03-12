using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;
using System.Linq;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet
{
    bool IsCollection
    (
        ref TypeMappingInfo map,
        out CollectionInfo targetColl,
        out CollectionInfo sourceColl,
        out CollectionMapping ltrInfo,
        out CollectionMapping rtlInfo
    )
    {
        if (map.IsCollection is false)
        {
            SetDefaults(out targetColl, out sourceColl, out ltrInfo, out rtlInfo);
            return false;
        }

        if (map.IsCollection is true)
        {
            targetColl = map.TargetType.CollectionInfo;
            sourceColl = map.SourceType.CollectionInfo;
            ltrInfo = map.TTSCollection;
            rtlInfo = map.STTCollection;
            return true;
        }
        else
        {
            SetDefaults(out targetColl, out sourceColl, out ltrInfo, out rtlInfo);

            if (map.TargetType.IsIterable is false || map.SourceType.IsIterable is false)
                return false;


            if (map.TargetType.IsIterable is true)
            {
                targetColl = map.TargetType.CollectionInfo;
            }
            else if (map.TargetType.IsIterable is null
                && true == (map.TargetType.IsIterable = IsEnumerableType(map.TargetType.NonGenericFullName, map.TargetType._typeSymbol, out targetColl)))
            {
                map.TargetType.CollectionInfo = targetColl;
            }
            else
                return false;

            if (map.SourceType.IsIterable is true)
            {
                sourceColl = map.SourceType.CollectionInfo;
            }
            else if (map.SourceType.IsIterable is null
                && true == (map.SourceType.IsIterable = IsEnumerableType(map.SourceType.NonGenericFullName, map.SourceType._typeSymbol, out sourceColl)))
            {
                map.SourceType.CollectionInfo = sourceColl;
            }
            else
                return false;

            if (map.IsCollection is null)
            {
                ltrInfo = map.TTSCollection = GetResult(targetColl, sourceColl, map.ToSourceMethodName, map.FillSourceMethodName);
                rtlInfo = map.STTCollection = GetResult(sourceColl, targetColl, map.ToTargetMethodName, map.FillTargetMethodName);
                map.IsCollection = true;
                return true;
            }

            map.IsCollection = false;

            return false;
        }

        static void SetDefaults(out CollectionInfo targetColl, out CollectionInfo sourceColl, out CollectionMapping ltrInfo, out CollectionMapping rtlInfo)
        {
            ltrInfo = default;
            rtlInfo = default;
            targetColl = default!;
            sourceColl = default!;
        }
    }

    private static CollectionInfo GetCollectionInfo(EnumerableType enumerableType, ITypeSymbol typeSymbol)
        => enumerableType switch
        {
#pragma warning disable format
        EnumerableType.Queue =>
            new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), false, true, true,   false,  "Enqueue",  "Count"),
        EnumerableType.Stack =>
            new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), false, true, true,   false,  "Push",     "Count"),
        EnumerableType.Enumerable =>
            new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), false, true, false,  true,   null,       "Length"),
        EnumerableType.ReadOnlyCollection =>
            new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, true,  true,   false,  "Add",      "Count"),
        EnumerableType.ReadOnlySpan =>
            new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, true,  true,   true,   null,       "Length"),
        EnumerableType.Collection =>
            new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, false, true,   false,  "Add",      "Count"),
        EnumerableType.Span =>
            new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, false, true,   true,   null,       "Length"),
        _ =>
            new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, false, true,   true,   null,       "Length")
#pragma warning restore format
    };

    static CollectionMapping GetResult(CollectionInfo a, CollectionInfo b, string toMethodName, string fillMethodName)
    {
        var redim = !a.Countable && b.BackingArray;

        var iterator = a.Indexable && b.BackingArray ? "for" : "foreach";

        return new(b.BackingArray, b.BackingArray && !a.Indexable, iterator, redim, b.Method, toMethodName, fillMethodName);
    }

    bool IsEnumerableType(string fullNonGenericName, ITypeSymbol type, out CollectionInfo info)
    {
        if (type.IsPrimitive(true))
        {
            info = default!;
            return false;
        }

        switch (fullNonGenericName)
        {
            case "global::System.Collections.Generic.Stack"
            :
                info = GetCollectionInfo(EnumerableType.Stack, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                info = GetCollectionInfo(EnumerableType.Queue, GetEnumerableType(compilation, type));

                return true;

            case "global::System.ReadOnlySpan"
            :

                info = GetCollectionInfo(EnumerableType.ReadOnlySpan, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Span"
            :
                info = GetCollectionInfo(EnumerableType.Span, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                info = GetCollectionInfo(EnumerableType.Collection, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                info = GetCollectionInfo(EnumerableType.ReadOnlyCollection, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                info = GetCollectionInfo(EnumerableType.Enumerable, GetEnumerableType(compilation, type));

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

        static ITypeSymbol GetEnumerableType(Compilation compilation, ITypeSymbol enumerableType)
        {
            return ((INamedTypeSymbol)enumerableType).TypeArguments.FirstOrDefault() ?? compilation.ObjectType;
        }
    }
}
