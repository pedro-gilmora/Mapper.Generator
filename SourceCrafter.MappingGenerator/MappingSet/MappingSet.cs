using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Text;
using SourceCrafter.Bindings.Helpers;

// ReSharper disable once CheckNamespace
namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet(Compilation compilation, TypeSet typeSet) 
    : Set<int, TypeMapping>(m => GetId(m.SourceType.Id, m.TargetType.Id))
{
    private short _targetScopeId, _sourceScopeId;

    private readonly bool 
        /*canOptimize = compilation.GetTypeByMetadataName("System.Span`1") is not null,*/
        _canUseUnsafeAccessor = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;

    private const string
        TupleStart = "(",
        TupleEnd = " )",
        TypeStart = @"new {0}
        {{",
        TypeEnd = @"
        }";

    internal void TryAdd(
        ITypeSymbol sourceTypeSymbol,
        ITypeSymbol targetTypeSymbol,
        ApplyOn ignore,
        MappingKind mapKind,
        Action<string, string> addSource)
    {
        TypeMeta targetType = typeSet.GetOrAdd(targetTypeSymbol),
            sourceType = typeSet.GetOrAdd(sourceTypeSymbol);

        var typeMapping = BuildMap(targetType, sourceType);

        if (typeMapping.AreSameType) return;

        BuildMap(targetType, targetType);
        BuildMap(sourceType, sourceType);

        TypeMapping BuildMap(TypeMeta targetTypeData, TypeMeta sourceTypeData)
        {
            MemberMeta 
                targetMember = new(++_targetScopeId, "to", targetTypeSymbol.IsNullable()), 
                sourceMember = new(--_sourceScopeId, "source", sourceTypeSymbol.IsNullable());

            var mapping = GetOrAdd(targetTypeData, sourceTypeData);

            mapping.MappingsKind = mapKind;

            mapping = DiscoverTypeMaps(
                mapping,
                sourceMember,
                targetMember,
                ignore,
                "+",
                "+");

            StringBuilder code = new(@"#nullable enable
namespace SourceCrafter.Bindings;

public static partial class BindingExtensions
{");
            var len = code.Length;

            mapping.BuildMethods(code);

            if (code.Length == len)
                return mapping;

            var id = mapping.GetFileName();

            addSource(id, code.Append(@"
}").ToString());


            return mapping;
        }
    }

    private TypeMapping GetOrAdd(TypeMeta target, TypeMeta source)
    {
        var hashCode = GetId(target.Id, source.Id);
        ref var item = ref GetOrAddDefault(hashCode, out bool exists);

        if (exists) return item!;

        return item = new(hashCode, target, source);
    }

    private static int GetId(int targetId, int sourceId) => 
        (Math.Min(targetId, sourceId), Math.Max(targetId, sourceId)).GetHashCode();
}

internal readonly record struct TypeImplInfo(ITypeSymbol MembersSource, ITypeSymbol? Implementation = null);

internal readonly record struct MapInfo(ITypeSymbol From, ITypeSymbol To, MappingKind MapKind, ApplyOn Ignore, bool Generate = true);