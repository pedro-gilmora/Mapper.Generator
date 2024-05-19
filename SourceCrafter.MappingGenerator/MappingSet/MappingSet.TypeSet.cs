using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Helpers;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SourceCrafter.Bindings;

internal sealed class TypeSet(Compilation compilation)
{
    static readonly EqualityComparer<int> _intComparer = EqualityComparer<int>.Default;

    record struct TypeEntry(int Id) { internal TypeData type; internal int next = 0; }

    internal TypeData GetOrAdd(ITypeSymbol typeSymbol, bool dictionaryOwned = false)
    {
        var entries = _entries;

        var info = GetTypeMapInfo(typeSymbol);

        uint collisionCount = 0;

        int hashCode = GetId(info.Implementation ?? info.MembersSource);

        ref int bucket = ref GetBucket((uint)hashCode);

        int i = bucket - 1; // Value in _buckets is 1-based

        while (true)
        {
            // Should be a while loop https://github.com/dotnet/runtime/issues/9422
            // Test uint in if rather than loop condition to drop range check for following array access
            if ((uint)i >= (uint)entries.Length)
            {
                break;
            }

            if (_intComparer.Equals(entries[i].Id, hashCode))
            {
                return entries[i].type;
            }

            i = entries[i].next;

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
            Debug.Assert(-3 - entries[_freeList].next >= -1, "Shouldn't overflow because [next] cannot underflow");
            _freeList = -3 - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            if (_count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket((uint)hashCode);
            }
            index = (int)_count;
            _count++;
            entries = _entries;
        }

        entries[index] = new(hashCode) { type = new(this, compilation, info, hashCode, dictionaryOwned), next = bucket - 1 /* Value in _buckets is 1-based */ };

        ref var entry = ref entries[index];

        bucket = index + 1; // Value in _buckets is 1-based

        _version++;

        return entry.type;
    }

    static TypeImplInfo GetTypeMapInfo(ITypeSymbol targetSymbol)
    {
        return targetSymbol is INamedTypeSymbol { } namedSymbol && namedSymbol.ToGlobalNonGenericNamespace() == "global::SourceCrafter.Bindings.Attributes.IImplement"
            ? new(namedSymbol.TypeArguments[0], namedSymbol.TypeArguments[1])
            : new(targetSymbol, null);
    }

    internal static int GetId(ITypeSymbol type) =>
        MappingSet._comparer.GetHashCode(type.Name == "Nullable"
            ? ((INamedTypeSymbol)type).TypeArguments[0]
            : type);

    #region Dictionary Implementation

    uint _count = 0;

    TypeEntry[] _entries = [];

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
        var prime = GetPrime(capacity);
        var buckets = new int[prime];
        var entries = new TypeEntry[prime];

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
    private static uint FastMod(uint value, uint divisor, ulong multiplier) => (uint)(((multiplier * value >> 32) + 1) * divisor >> 32);

    private void Resize() => Resize(ExpandPrime(_count));

    private void Resize(uint newSize)
    {
        var array = new TypeEntry[newSize];

        Array.Copy(_entries, array, _count);

        _buckets = new int[newSize];
        _fastModMultiplier = GetFastModMultiplier(newSize);

        for (int j = 0; j < _count; j++)
        {
            if (array[j].next >= -1)
            {
                ref int bucket = ref GetBucket((uint)array[j].Id);
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

    #endregion
}