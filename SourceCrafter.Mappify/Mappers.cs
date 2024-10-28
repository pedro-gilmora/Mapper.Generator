using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.Helpers;

namespace SourceCrafter.Mappify;

internal sealed partial class Mappers : Set<int, TypeMap>
{
    internal readonly TypeSet Types;
    internal readonly bool CanUseUnsafeAccessor;

    public Mappers(Compilation compilation, ImmutableArray<Mapping> bindAttrs) : base(m => m.Id)
    {
        CanUseUnsafeAccessor = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;
        
        Types = new(compilation);

#pragma warning disable IDE0042 // Deconstruct variable declaration
        foreach (var item in bindAttrs) GetOrAdd(item.a, item.b, item.ignore);
#pragma warning restore IDE0042 // Deconstruct variable declaration
    }

    internal TypeMap GetOrAdd(TypeMeta source, TypeMeta target, Applyment ignore, bool isSourceNullable = false, bool isTargetNullable = false)
    {
        return GetOrAdd(source.Symbol, target.Symbol, ignore);
    }

    internal TypeMap GetOrAdd(MemberMeta source, MemberMeta target, Applyment ignore)
    {
        return GetOrAdd(source.Type.Symbol, source.Type.Symbol, ignore, source.IsNullable, target.IsNullable);
    }

    private TypeMap GetOrAdd(ITypeSymbol source, ITypeSymbol target, Applyment ignore, bool isSourceNullable = false, bool isTargetNullable = false)
    {
        var (sourceId, targetId) = (SymbolEqualityComparer.Default.GetHashCode(source), SymbolEqualityComparer.Default.GetHashCode(target));

        #region Check existence

        var mapperId = TypeMap.GetId(sourceId, targetId);

        ref var mapper = ref GetOrAddDefault(mapperId, out var exists);

        if (exists)
        {
            mapper.AddSourceTryGet |= isSourceNullable;
            mapper.AddTargetTryGet |= isTargetNullable;

            return mapper;
        }

        #endregion

        #region Source

        var typeMapId = TypeMap.GetId(sourceId, sourceId);

        ref var sourceType = ref Types.GetOrAddDefault(typeMapId, out var existA);

        if (!existA) sourceType = new(Types, sourceId, source);

        ref var sourceMapper = ref GetOrAddDefault(typeMapId, out exists);

        if (!exists)
        {
            new TypeMap(this, ref sourceMapper, typeMapId, sourceType, sourceType, Applyment.None) { AddSourceTryGet = isSourceNullable };
        }
        else
        {
            sourceMapper.AddSourceTryGet |= isSourceNullable;
        }

        #endregion

        #region Returns if is same type mapper

        if (sourceId == targetId)
        {
            sourceMapper.AddSourceTryGet |= isTargetNullable;

            return sourceMapper; // Is same type, so same type mapper will be returned
        }

        #endregion

        #region Target

        ref var targetType = ref Types.GetOrAddDefault(targetId, out exists);

        if (!exists) targetType = new(Types, targetId, target);

        typeMapId = TypeMap.GetId(targetId, targetId);

        ref var targetMapper = ref GetOrAddDefault(typeMapId, out exists);

        if (!exists)
        {
            new TypeMap(this, ref targetMapper, typeMapId, targetType, targetType, Applyment.None) { AddSourceTryGet = isTargetNullable };
        }
        else
        {
            targetMapper.AddTargetTryGet |= isTargetNullable;
        }

        #endregion

        #region Final Mapper

        return new (this, ref mapper, mapperId, sourceType, targetType, ignore)
        {
            AddSourceTryGet = isSourceNullable,
            AddTargetTryGet = isTargetNullable
        };

        #endregion
    }
}