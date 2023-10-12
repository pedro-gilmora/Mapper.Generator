using Microsoft.CodeAnalysis;

namespace SourceCrafter.Mapping;

internal record struct IterInfo()
{
    internal ITypeSymbol Type { get; set; }
    internal string? CountProperty { get; set; }
    internal IterableType IterableType { get; set; } = IterableType.Collection;
    internal string ItemFullTypeName { get; set; }
    internal string AddMethod { get; set; } = @"{0}.Add({1});";
}
