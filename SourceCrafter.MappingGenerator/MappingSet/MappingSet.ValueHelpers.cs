using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Helpers;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace SourceCrafter.Bindings;

internal delegate void ValueBuilder(StringBuilder code, string val);

internal sealed partial class MappingSet
{
    static void GetNullability(MemberMetadata target, ITypeSymbol targetType, MemberMetadata source, ITypeSymbol sourceType)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string? Exch(ref string? init, string? update = null) => ((init, update) = (update, init)).update;

    static void GenerateValue
    (
        StringBuilder code,
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
            ? "_" + (hasIndexer ? item[..indexerBracketPos] : item).Replace(".", "")
            : item;

        if (checkNull)
        {
            if (call)
            {
                code.Append(item).Append(" is {} ").Append(itemCache).Append(" ? ");

                generateValue(code, itemCache);
                
                code.Append(" : default").Append(defaultSourceBang);
            }
            else
            {
                if (isValueType && defaultSourceBang != null)
                {
                    int startIndex = code.Length;

                    generateValue(code, itemCache);

                    if (code[startIndex] == '(')
                    {
                        int count = code.Length - startIndex;

                        code.Replace(item, "(" + item, startIndex, count)
                            .Insert(startIndex + count + 1, " ?? default")
                            .Append(defaultSourceBang).Append(")");
                    }
                }
                else
                {
                    generateValue(code, item);

                    code.Append(sourceBang);
                }
            }
        }
        else
        {
            generateValue(code, itemCache);
            code.Append(sourceBang);
        }
    }
}
