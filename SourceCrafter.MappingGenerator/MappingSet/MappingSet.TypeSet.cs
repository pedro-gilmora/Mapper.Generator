﻿using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Helpers;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SourceCrafter.Bindings;

internal sealed class TypeSet(Compilation compilation) : Set<int, TypeMeta>(m => m.Id)
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private record struct TypeEntry(int Id) { internal TypeMeta Type; internal int Next = 0; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    internal TypeMeta GetOrAdd(ITypeSymbol typeSymbol, bool dictionaryOwned = false)
    {
        GetTypeMapInfo(typeSymbol, out var membersSource, out var implementation);

        var hashCode = GetId(implementation ?? membersSource);

        ref var item = ref GetOrAddDefault(hashCode, out var exists);

        if(exists) return item!;

        return item = new(this, compilation, membersSource, implementation, hashCode, dictionaryOwned);
    }

    private static void GetTypeMapInfo(ITypeSymbol targetSymbol, out ITypeSymbol memberSource, out ITypeSymbol? implementation)
    {
        (memberSource, implementation) = targetSymbol is INamedTypeSymbol { } namedSymbol && namedSymbol.ToGlobalNonGenericNamespace() == "global::SourceCrafter.Bindings.Attributes.IImplement"
            ? (namedSymbol.TypeArguments[0], namedSymbol.TypeArguments[1])
            : (targetSymbol, null);
    }

    internal static int GetId(ITypeSymbol type) =>
        SymbolEqualityComparer.Default.GetHashCode(type.Name == "Nullable"
            ? ((INamedTypeSymbol)type).TypeArguments[0]
            : type);
}