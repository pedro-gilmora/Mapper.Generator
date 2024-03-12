using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;
using System.Linq;

namespace SourceCrafter.Bindings;

internal delegate string ValueBuilder(string val);

internal sealed partial class MappingSet
{
    static void GetNullability(Member target, ITypeSymbol targetType, Member source, ITypeSymbol sourceType)
    {
        source.DefaultBang = GetDefaultBangChar(target.IsNullable, source.IsNullable, sourceType.AllowsNull());
        source.Bang = GetBangChar(target.IsNullable, source.IsNullable);
        target.DefaultBang = GetDefaultBangChar(source.IsNullable, target.IsNullable, targetType.AllowsNull());
        target.Bang = GetBangChar(source.IsNullable, target.IsNullable);
    }

    static string? GetDefaultBangChar(bool isTargetNullable, bool isSourceNullable, bool sourceAllowsNull)
        => !isTargetNullable && (sourceAllowsNull || isSourceNullable) ? "!" : null;

    static string? GetBangChar(bool isTargetNullable, bool isSourceNullable)
        => !isTargetNullable && isSourceNullable ? "!" : null;

    static string? Exch(ref string? init, string? update = null) => ((init, update) = (update, init)).update;

    static string GenerateValue
    (
        string item,
        ValueBuilder generateValue,
        bool checkNull,
        bool call,
        bool isValueType,
        string? sourceBang,
        string? defaultSourceBang
    )
    {
        var indexerBracketPos = item.IndexOf('[');
        bool hasIndexer = indexerBracketPos > -1,
            shouldCache = checkNull && call && (hasIndexer || item.Contains('.'));
        var itemCache = shouldCache
            ? (hasIndexer ? "_" + item[..indexerBracketPos] : item).Replace(".", "")
            : item;

        return checkNull
            ? call
              ? $"{item} is {{}} {itemCache} ? {generateValue(itemCache)} : default{defaultSourceBang}"
              : isValueType && defaultSourceBang != null
                ? buildNullCoalesceDefault(generateValue(itemCache) + " ?? default!")
                : generateValue(itemCache) + sourceBang
            : generateValue(item) + sourceBang;

        string buildNullCoalesceDefault(string value)
        {
            return value[0] == '('
                ? value.Replace(item, "(" + item) + ")"
                : value;
        }
    }
}
