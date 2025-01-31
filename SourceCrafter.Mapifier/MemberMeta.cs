﻿using Microsoft.CodeAnalysis;

using System.Collections.Immutable;

namespace SourceCrafter.Mapifier;

internal sealed record MemberMeta(
    int Id,
    string Name,
    bool IsNullable,
    bool IsReadOnly = false,
    bool IsWriteOnly = false,
    bool IsInit = false,
    bool IsProperty = false,
    ImmutableArray<AttributeData> Attributes = default)
{
    internal string? DefaultBang, Bang;

    internal short MaxDepth = 1;

    internal TypeMeta Type = null!;

    internal TypeMeta OwningType = null!;

    internal bool IsAutoProperty, CanBeInitialized = true, IsAccessible = true;

    internal int Position;

    public override string ToString() => 
        $"({Type?.ToString() ?? "?"}) {(OwningType?.ExportNotNullFullName is { } name ? name + "." : null)}{Name}";
}
