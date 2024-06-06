using Microsoft.CodeAnalysis;

using System.Collections.Immutable;

namespace SourceCrafter.Bindings;

internal sealed record MemberMetadata(
    int Id,
    string Name,
    bool IsNullable,
    bool IsReadOnly = false,
    bool IsWriteOnly = false,
    bool IsInit = false,
    bool CanMap = true,
    bool IsProperty = false,
    ImmutableArray<AttributeData> Attributes = default)
{
    internal string? DefaultBang, Bang;

    internal short MaxDepth = 1;

    internal TypeMetadata Type = null!;

    internal TypeMetadata? OwningType;

    internal bool IsAutoProperty, CanBeInitialized = true, IsAccessible = true;

    internal int Position;

    internal bool MaxDepthReached(string pathStr)
    {
        if (MaxDepth == 0)
            return true;

        var id = $"+{Id}+";
        int idLength = id.Length, end = pathStr.Length, depth = MaxDepth;

        while (end >= idLength)
        {
            if (pathStr[(end - idLength)..end] == id)
            {
                if (--depth == 0)
                    return true;

                end -= idLength;
            }
            else
            {
                end--;
            }
        }

        return false;
    }

    public override string ToString() => 
        $"({(Type?.ToString() ?? "?")}) {(OwningType?.ExportNotNullFullName is { } name ? name+"." : null)}{Name}";
}
