using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using SourceCrafter.Bindings.Helpers;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet(Compilation compilation, TypeSet typeSet)
{
    private short _targetScopeId, _sourceScopeId;

    private readonly bool /*canOptimize = compilation.GetTypeByMetadataName("System.Span`1") is not null,
        */_canUseUnsafeAccessor = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;

    private const string
        TupleStart = "(",
        TupleEnd = " )",
        TypeStart = @"new {0}
        {{",
        TypeEnd = @"
        }";

    private static readonly EqualityComparer<uint> UintComparer = EqualityComparer<uint>.Default;

    internal static readonly SymbolEqualityComparer Comparer = SymbolEqualityComparer.Default;

    internal void AddMapper(ITypeSymbol sourceType, ITypeSymbol targetType, ApplyOn ignore, MappingKind mapKind, Action<string, string> addSource)
    {
        TypeMeta targetTypeData = typeSet.GetOrAdd(targetType),
            sourceTypeData = typeSet.GetOrAdd(sourceType);

        var typeMapping = BuildMap(targetTypeData, sourceTypeData);

        if (!typeMapping.AreSameType)
        {
            BuildMap(targetTypeData, targetTypeData);
            BuildMap(sourceTypeData, sourceTypeData);
        }

        return;

        TypeMapping BuildMap(TypeMeta targetTypeData, TypeMeta sourceTypeData)
        {

            MemberMeta 
                target = new(++_targetScopeId, "to", targetType.IsNullable()), 
                source = new(--_sourceScopeId, "source", sourceType.IsNullable());

            var mapping = GetOrAddMapper(targetTypeData, sourceTypeData);

            mapping.MappingsKind = mapKind;

            mapping = ParseTypesMap(
                mapping,
                source,
                target,
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

            var id = mapping.GetMappingHash();

            addSource(id, code.Append(@"
}").ToString());


            return mapping;
        }
    }

    private TypeMapping GetOrAddMapper(TypeMeta target, TypeMeta source)
    {
        var entries = Entries;

        var hashCode = GetId(target.Id, source.Id);

        uint collisionCount = 0;
        ref var bucket = ref GetBucket(hashCode);
        var i = bucket - 1; // Value in _buckets is 1-based

        ref var entry = ref Unsafe.NullRef<TypeMappingEntry>();

        while (true)
        {
            // Should be a while loop https://github.com/dotnet/runtime/issues/9422
            // Test uint in if rather than loop condition to drop range check for following array access
            if ((uint)i >= (uint)entries.Length)
            {
                break;
            }

            if (UintComparer.Equals((entry = ref entries[i]).Id, hashCode))
            {
                return entry.Info;
            }

            entry.Info.CollectTarget(ref target, target.Id);
            entry.Info.CollectSource(ref source, source.Id);

            i = entry.next;

            collisionCount++;

            if (collisionCount > (uint)entries.Length)
            {
                // The chain of entries forms a loop; which means a concurrent update has happened.
                // Break out of the loop and throw, rather than looping forever.
                throw new NotSupportedException("Concurrent operations are not allowed");
            }


        }

        int index;
        if (_freeCount > 0)
        {
            index = _freeList;
            Debug.Assert((-3 - entries[_freeList].next) >= -1, "shouldn't overflow because `next` cannot underflow");
            _freeList = -3 - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            if (Count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket(hashCode);
            }
            index = (int)Count;
            Count++;
            entries = Entries;
        }

        entries[index] = new(hashCode, bucket - 1, new(hashCode, target, source));

        entry = ref entries[index];

        entry.next = bucket - 1; // Value in _buckets is 1-based

        bucket = index + 1; // Value in _buckets is 1-based

        _version++;

        return entry.Info;
    }

    internal static int GetId(ITypeSymbol type) => 
        Comparer.GetHashCode(type.Name == "Nullable"
            ? ((INamedTypeSymbol)type).TypeArguments[0]
            : type);

    private static uint GetId(int targetId, int sourceId) => (uint)(Math.Min(targetId, sourceId), Math.Max(targetId, sourceId)).GetHashCode();

    #region Dictionary Implementation

    internal uint Count = 0;

    internal TypeMappingEntry[] Entries = [];

    private int[] _buckets = null!;
    private static readonly uint[] SPrimes = [3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919, 1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437, 187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263, 1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369];


    private int _freeList;
    private ulong _fastModMultiplier;
    private int _version, _freeCount;

    private static uint GetPrime(uint min)
    {
        var array = SPrimes;

        foreach (var num in array)
            if (num >= min)
                return num;

        for (var j = min | 1u; j < uint.MaxValue; j += 2)
            if (IsPrime(j) && (j - 1) % 101 != 0)
                return j;

        return min;
    }

    private static bool IsPrime(uint candidate)
    {
        if ((candidate & (true ? 1u : 0u)) != 0)
        {
            var num = Math.Sqrt(candidate);

            for (var i = 3; i <= num; i += 2)
                if (candidate % i == 0)
                    return false;

            return true;
        }
        return candidate == 2;
    }

    internal uint Initialize(uint capacity)
    {
        typeSet.Initialize(0);
        var prime = GetPrime(capacity);
        var buckets = new int[prime];
        var entries = new TypeMappingEntry[prime];

        _freeList = -1;
#if TARGET_64BIT
        _fastModMultiplier = GetFastModMultiplier(prime);
#endif

        _buckets = buckets;
        Entries = entries;
        return prime;
    }

    private static ulong GetFastModMultiplier(uint divisor) => ulong.MaxValue / divisor + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetBucket(uint hashCode)
    {
        var buckets = _buckets;
        return ref buckets[FastMod(hashCode, (uint)buckets.Length, _fastModMultiplier)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FastMod(uint value, uint divisor, ulong multiplier) 
        => (uint)(((multiplier * value >> 32) + 1) * divisor >> 32);

    private void Resize() => Resize(ExpandPrime(Count));

    private void Resize(uint newSize)
    {
        var array = new TypeMappingEntry[newSize];

        Array.Copy(Entries, array, Count);

        _buckets = new int[newSize];
        _fastModMultiplier = GetFastModMultiplier(newSize);

        for (var j = 0; j < Count; j++)
        {
            if (array[j].next >= -1)
            {
                ref var bucket = ref GetBucket(array[j].Id);
                array[j].next = bucket - 1;
                bucket = j + 1;
            }
        }
        Entries = array;
    }

    private static uint ExpandPrime(uint oldSize)
    {
        var num = 2 * oldSize;
        if (num > 2147483587u && 2147483587u > oldSize)
        {
            return 2147483587u;
        }
        return GetPrime(num);
    }

    internal static bool AreTypeEquals(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        return Comparer.Equals(sourceType, targetType);
    }
    #endregion
}

internal readonly record struct TypeImplInfo(ITypeSymbol MembersSource, ITypeSymbol? Implementation = null);

internal readonly record struct MapInfo(ITypeSymbol From, ITypeSymbol To, MappingKind MapKind, ApplyOn Ignore, bool Generate = true);

internal readonly record struct CompilationAndAssemblies(Compilation Compilation, ImmutableArray<MapInfo> FromClasses, ImmutableArray<MapInfo> FromAssemblyInfo);