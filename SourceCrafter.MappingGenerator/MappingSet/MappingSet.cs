using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using SourceCrafter.Bindings.Helpers;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet(Compilation compilation, TypeSet typeSet)
{
    short targetScopeId, sourceScopeId;

    readonly bool /*canOptimize = compilation.GetTypeByMetadataName("System.Span`1") is not null,
        */canUseUnsafeAccessor = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;

    private const string
        TUPLE_START = "(",
        TUPLE_END = " )",
        TYPE_START = @"new {0}
        {{",
        TYPE_END = @"
        }";

    static readonly EqualityComparer<uint> _uintComparer = EqualityComparer<uint>.Default;

    internal static readonly SymbolEqualityComparer
        _comparer = SymbolEqualityComparer.Default;

    internal void AddMapper(TypeMapInfo sourceType, TypeMapInfo targetType, ApplyOn ignore, MappingKind mapKind, Action<string, string> addSource)
    {
        ITypeSymbol targetTypeSymbol = targetType.implementation ?? targetType.membersSource,
            sourceTypeSymbol = sourceType.implementation ?? sourceType.membersSource;

        int targetId = GetId(targetTypeSymbol),
            sourceId = GetId(sourceTypeSymbol);

        Member
            target = new(++targetScopeId, "to", targetTypeSymbol.IsNullable()),
            source = new(--sourceScopeId, "source", sourceTypeSymbol.IsNullable());

        var typeMappingInfo = GetOrAddMapper(
            targetType,
            sourceType,
            targetId,
            sourceId);

        typeMappingInfo.MappingsKind = mapKind;

        typeMappingInfo = ParseTypesMap(
            typeMappingInfo,
            source,
            target,
            ignore,
            "+",
            "+");

        typeMappingInfo.BuildMethods(addSource);
    }

    TypeMappingInfo GetOrAddMapper(TypeMapInfo target, TypeMapInfo source, int targetId, int sourceId)
    {
        var entries = _entries;

        var hashCode = GetId(targetId, sourceId);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket(hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based

        ref var entry = ref Unsafe.NullRef<TypeMapping>();

        TypeData? targetTD = null, sourceTD = null;

        while (true)
        {
            // Should be a while loop https://github.com/dotnet/runtime/issues/9422
            // Test uint in if rather than loop condition to drop range check for following array access
            if ((uint)i >= (uint)entries.Length)
            {
                break;
            }

            if (_uintComparer.Equals((entry = ref entries[i]).Id, hashCode))
            {
                return entry.Info;
            }

            entry.Info.GatherTarget(ref targetTD, targetId);
            entry.Info.GatherSource(ref sourceTD, sourceId);

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
            if (_count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket(hashCode);
            }
            index = (int)_count;
            _count++;
            entries = _entries;
        }

        entries[index] = new(
            hashCode,
            bucket - 1,
            new(hashCode,
                targetTD ??= typeSet.GetOrAdd(target, targetId),
                sourceTD ??= typeSet.GetOrAdd(source, sourceId)));

        entry = ref entries[index];

        entry.next = bucket - 1; // Value in _buckets is 1-based

        bucket = index + 1; // Value in _buckets is 1-based

        _version++;

        return entry.Info;
    }

    internal static int GetId(ITypeSymbol type) => 
        _comparer.GetHashCode(type.Name == "Nullable"
            ? ((INamedTypeSymbol)type).TypeArguments[0]
            : type);

    static uint GetId(int targetId, int sourceId) => (uint)(Math.Min(targetId, sourceId), Math.Max(targetId, sourceId)).GetHashCode();

    #region Dictionary Implementation

    internal uint _count = 0;

    internal TypeMapping[] _entries = [];

    private int[] _buckets = null!;
    private static readonly uint[] s_primes = [3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919, 1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437, 187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263, 1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369];


    private int _freeList;
    private ulong _fastModMultiplier;
    private int _version, _freeCount;

    private static uint GetPrime(uint min)
    {
        uint[] array = s_primes;

        foreach (uint num in array)
            if (num >= min)
                return num;

        for (uint j = min | 1u; j < uint.MaxValue; j += 2)
            if (IsPrime(j) && (j - 1) % 101 != 0)
                return j;

        return min;
    }

    private static bool IsPrime(uint candidate)
    {
        if ((candidate & (true ? 1u : 0u)) != 0)
        {
            var num = Math.Sqrt(candidate);

            for (int i = 3; i <= num; i += 2)
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
        var entries = new TypeMapping[prime];

        _freeList = -1;
#if TARGET_64BIT
        _fastModMultiplier = GetFastModMultiplier(prime);
#endif

        _buckets = buckets;
        _entries = entries;
        return prime;
    }

    private static ulong GetFastModMultiplier(uint divisor) => ulong.MaxValue / divisor + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetBucket(uint hashCode)
    {
        int[] buckets = _buckets;
        return ref buckets[FastMod(hashCode, (uint)buckets.Length, _fastModMultiplier)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FastMod(uint value, uint divisor, ulong multiplier) 
        => (uint)(((multiplier * value >> 32) + 1) * divisor >> 32);

    private void Resize() => Resize(ExpandPrime(_count));

    private void Resize(uint newSize)
    {
        var array = new TypeMapping[newSize];

        Array.Copy(_entries, array, _count);

        _buckets = new int[newSize];
        _fastModMultiplier = GetFastModMultiplier(newSize);

        for (int j = 0; j < _count; j++)
        {
            if (array[j].next >= -1)
            {
                ref int bucket = ref GetBucket(array[j].Id);
                array[j].next = bucket - 1;
                bucket = j + 1;
            }
        }
        _entries = array;
    }

    private static uint ExpandPrime(uint oldSize)
    {
        uint num = 2 * oldSize;
        if (num > 2147483587u && 2147483587u > oldSize)
        {
            return 2147483587u;
        }
        return GetPrime(num);
    }

    internal static bool AreTypeEquals(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        return _comparer.Equals(sourceType, targetType);
    }
    #endregion
}

internal readonly record struct TypeMapInfo(ITypeSymbol membersSource, ITypeSymbol? implementation = null);

internal readonly record struct MapInfo(TypeMapInfo from, TypeMapInfo to, MappingKind mapKind, ApplyOn ignore, bool generate = true);

internal readonly record struct CompilationAndAssemblies(Compilation Compilation, ImmutableArray<MapInfo> OverClass, ImmutableArray<MapInfo> Assembly);