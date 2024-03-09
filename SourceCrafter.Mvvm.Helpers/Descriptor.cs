using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;

using SourceCrafter.Binding.Attributes;
using System.Linq;

using ConversionInfo = (bool IsImplicit, bool IsExplicit, bool IsReference);
using Microsoft.Win32.SafeHandles;
using System.Collections;

namespace SourceCrafter.Mvvm.Helpers
{
    internal delegate void CodeBuilder(StringBuilder code, string left, string right, Indent indent, ref string? comma, int scopeId);

    internal readonly struct MemberMapping
    {
        private readonly bool
            _hasEqualNames,
            _ignoresLeft,
            _ignoresRight;

        internal readonly bool
            CanMap,
            CanReverseMap;

        private readonly int
            _leftToRightId,
            _rightToLeftId;

        readonly TypeMapping _typeMapping;

        private readonly string
            _leftStrong,
            _defaultLeftStrong,
            _rightStrong,
            _defaultRightStrong;
        private readonly uint _parentMappingId;
        private readonly SymbolsMap _map;
        private readonly Member _left, _right;
        private readonly byte LeftThreshold, RightThreshold;

        private readonly ConversionInfo LTRConversion, RTLConversion;

        internal MemberMapping(uint parentMappingId, SymbolsMap map, Member left, Member right, TypeMapping entry)
        {
            _parentMappingId = parentMappingId;
            _map = map;
            _left = left;
            _right = right;

            _typeMapping = map.GetOrAddMapper(left.Type._type, right.Type._type, id => id == parentMappingId, out bool exists);
            
            _hasEqualNames = left.Name == right.Name;

            //alreadyMapping = left.Type.Equals(right.Type) && parentId == SymbolsMap.GetMappingId(left.Type, right.Type);
            _leftStrong = GetStrong(left, right);
            _defaultLeftStrong = GetDefaultStrong(left, right);
            _rightStrong = GetStrong(right, left);
            _defaultRightStrong = GetDefaultStrong(right, left);
            CanMap = CheckMapping(left, right, ref LeftThreshold, ref _ignoresRight);
            CanReverseMap = CheckMapping(right, left, ref RightThreshold, ref _ignoresLeft);
            _leftToRightId = (left.Id, right.Id).GetHashCode();
            _rightToLeftId = (right.Id, left.Id).GetHashCode();
            //areEnumerables = left.Type.IsEnumerable && right.Type.IsEnumerable;

            LTRConversion = map.GetConversion(left.Type, right.Type);
            RTLConversion = map.GetConversion(right.Type, left.Type);
        }

        private bool CheckMapping(Member left, Member right, ref byte leftThreshold, ref bool ignoresRight)
        {
            if (!left.IsWritable || !right.IsReadable)
                return false;

            if (_hasEqualNames)
                return true;

            var match = false;

            Ignore ignore;

            foreach (var attr in left.Attributes)
            {
                if (attr.AttributeClass?.ToDisplayString() is not { } className) continue;

                if (className == "SourceCrafter.Mapping.Attributes.IgnoreAttribute")
                {
                    if ((ignore = (Ignore)(int)attr.ConstructorArguments[0].Value!) is Ignore.This or Ignore.Both)
                        return false;
                    ignoresRight = ignore is Ignore.This or Ignore.Both;
                }

                if (className != "SourceCrafter.Mapping.Attributes.ThresholdAttribute")
                {
                    leftThreshold = (byte)attr.ConstructorArguments[1].Value!;
                    continue;
                }

                if (className != "SourceCrafter.Mapping.Attributes.MapAttribute") continue;

                if ((ignore = (Ignore)(int)attr.ConstructorArguments[0].Value!) is Ignore.This or Ignore.Both)
                    return false;

                ignoresRight = ignore is Ignore.This or Ignore.Both;

                match |= (attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0].Expression
                    is InvocationExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                        ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                    }
                    && _map.GetSymbol(id) is ISymbol member
                    && _Type.GetHashCode(member) == right.Id;
            }

            return match;
        }

        private static string GetStrong(Member left, Member right)
        {
            return !left.Type.IsNullable && right.Type.IsNullable ? "!" : "";
        }

        private static string GetDefaultStrong(Member left, Member right)
        {
            return !left.Type.IsNullable && (right.Type.AllowsNull || right.Type.IsNullable) ? "!" : "";
        }

        internal void TryBuildLeftToRight(ref bool hasMappings, ref CodeBuilder? initializers, ref CodeBuilder? properties)
        {
            TryBuildMap(_left, _right, LeftThreshold, LTRConversion, _leftToRightId, _leftStrong, _defaultLeftStrong, ref hasMappings, ref initializers, ref properties);
        }

        internal void TryBuildRightToLeft(ref bool hasMappings, ref CodeBuilder? initializers, ref CodeBuilder? properties)
        {
            TryBuildMap(_right, _left, RightThreshold, RTLConversion, _rightToLeftId, _rightStrong, _defaultRightStrong, ref hasMappings, ref initializers, ref properties);
        }

        private void TryBuildMap(
            Member leftMember,
            Member rightMember,
            byte leftThreshold,
            ConversionInfo info,
            int parentScopeId,
            string strongChar,
            string defaultStrongChar,
            ref bool hasMappings,
            ref CodeBuilder? initializers,
            ref CodeBuilder? props)
        {
            CodeBuilder? _props = props;

            if (info.IsExplicit)
            {
                hasMappings = true;
                _props += info.IsReference
                //If is by ref
                    ? (StringBuilder code, string left, string right, Indent indent, ref string? comma, int _) =>
                        code.AppendFormat(@"{0}
{1}    {2} = {3}.{4} as {5}", Interlocked.Exchange(ref comma, ","), indent, leftMember.Name, right, rightMember.Name, rightMember.Type.FullName.TrimEnd('?'))
                    //Is by value
                    : (StringBuilder code, string left, string right, Indent indent, ref string? comma, int _) => 
                        code.AppendFormat(@"{0}
{1}    {2} = ({3}){4}.{5}{6}", Interlocked.Exchange(ref comma, ","), indent, leftMember.Name, rightMember.Type, right, rightMember.Name, strongChar);

            }
            else if (info.IsImplicit)
            {
                hasMappings = true;
                _props += rightMember.Type.IsNullable
                    //build raw assignation
                    ? (StringBuilder code, string left, string right, Indent indent, ref string? comma, int _) =>
                code.AppendFormat(@"{0}
{1}    {2} = {3}.{4} is {{}} {3}{4} ? {3}{4} : default{5}", Interlocked.Exchange(ref comma, ","), indent, leftMember.Name, right, rightMember.Name, defaultStrongChar)

                    : (StringBuilder code, string left, string right, Indent indent, ref string? comma, int _) =>
                code.AppendFormat(@"{0}
{1}    {2} = {3}.{4}{5}", Interlocked.Exchange(ref comma, ","), indent, leftMember.Name, right, rightMember.Name, strongChar);
            }
            //else if (alreadyMapping)
            //{
            //    hasMappings = true;

            //    if(enumer)
            //}
            else if (_map.TryGetOrAddMapper(leftMember.Type._type, rightMember.Type._type, isAlreadyMapping(_parentMappingId, null!), out var type, out var itemInitializer, out var propsBuilder))
            {
                hasMappings = true;

                //initializers += itemInitializer;

                _props += (StringBuilder code, string left, string right, Indent indent, ref string? comma, int scopeId) =>
                {
                    BuildComplexProperty(code, parentScopeId, defaultStrongChar, left, right, _props, ref leftThreshold, ref indent, ref comma);
                };
            }
            //else if (areEnumerables)
            //{

            //}

            Func<uint, bool> isAlreadyMapping(uint pId, Func<uint, bool> parentLookUp)
            {
                return newId => pId == newId || parentLookUp(newId);
            }

            void BuildComplexProperty(StringBuilder code, int parentScopeId, string defaultStrongChar, string left, string right, CodeBuilder? _props, ref byte leftThreshold, ref Indent indent, ref string? comma)
            {
                code.AppendFormat(@"{0}
{1}    {2} = ", Interlocked.Exchange(ref comma, ","), indent, rightMember.Name);

                comma ??= ",";

                if (--leftThreshold >= 0)
                {
                    code.AppendFormat("default{0}", defaultStrongChar);
                }
                else
                {
                    indent++;

                    StartNullableType(code, leftMember.Type.FullName, left + leftMember.Name, right + '.' + rightMember.Name, ref comma, ref indent);

                    _props?.Invoke(code, left + leftMember.Name, right + rightMember.Name, indent, ref comma, leftMember.Id);

                    EndNullableType(code, indent, defaultStrongChar);

                    indent--;
                }
            }
            props = _props;

            void StartNullableType(StringBuilder code, string typeName, string left, string right, ref string? comma, ref Indent indent)
            {
                code.AppendFormat(@"{0} is {{}} {1} 
{2}    ? new {3}()
{2}    {{", left, right, indent, typeName.Replace("?", ""));

                comma = null;
                indent++;
            }

            void EndNullableType(StringBuilder code, Indent indent, string defaultRequired)
            {
                indent--;

                code.AppendFormat(@"
{0}    }}
{0}    : default{1}", indent, defaultRequired);
            }

            void StartType(StringBuilder code, string toTypeName, ref string? comma, ref Indent indent)
            {
                code.AppendFormat(@"new {0}()
    {1}    {{", toTypeName.Replace("?", ""), indent);

                comma = null;
                indent++;
            }

            void EndType(StringBuilder code, Indent indent)
            {
                indent--;

                code.AppendFormat(@"
    {0}    }}", indent);
            }

            //bool ReachedThreshold(int mappingId, int id)
            //{
            //    //throw new NotImplementedException();
                
            //}
        }
    }

    internal readonly struct Member(int id, bool isReadable, bool isWritable, string name, ITypeSymbol type, ImmutableArray<AttributeData> attributes, bool exists = true)
    {
        internal readonly int Id = id;
        internal readonly string Name = name;
        internal readonly _Type Type = new(type);
        internal readonly bool
            IsWritable = isReadable,
            IsReadable = isWritable,
            Exists = exists;
        internal readonly ImmutableArray<AttributeData> Attributes = attributes;

        public override string ToString() => $"({Type}) {Name}";
    }

    internal sealed class Indent
    {
        private string spaces = "    ";
        public static Indent operator ++(Indent from)
        {
            from.spaces += "    ";
            return from;
        }
        public static Indent operator +(Indent from, int i)
        {
            from.spaces += new string(' ', i * 4);
            return from;
        }
        public static Indent operator --(Indent from)
        {
            if (from.spaces.Length > 4)
                from.spaces = from.spaces[..^4];
            return from;
        }
        public static Indent operator -(Indent from, int i)
        {
            i *= 4;
            if (from.spaces.Length > i)
                from.spaces = from.spaces[..^i];
            return from;
        }

        public override string ToString() => spaces;
    }

    internal sealed class SymbolsMap(Compilation compilation)
    {
        internal uint _count = 0;

        internal TypeMapping[] _entries = [];

        private int[] _buckets = null!;
        private static readonly uint[] s_primes =
        [
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71,
            89, 107, 131, 163, 197, 239, 293, 353, 431, 521,
            631, 761, 919, 1103, 1327, 1597, 1931, 2333, 2801, 3371,
            4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519, 21023,
            25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363,
            156437, 187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403,
            968897, 1162687, 1395263, 1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559,
            5999471, 7199369
        ];


        private int _freeList;
        private ulong _fastModMultiplier;
        private int _version, _freeCount;

        static readonly EqualityComparer<uint> _uintComparer = EqualityComparer<uint>.Default;
        private readonly Compilation _compilation = compilation;

        internal ConversionInfo GetConversion(_Type leftType, _Type rightType)
        {
            var conv = _compilation.ClassifyConversion(leftType._type, rightType._type);
            return (conv.Exists && conv.IsImplicit, conv.Exists && conv.IsExplicit, conv.Exists && conv.IsReference);
        }

        public bool TryGetOrAddMapper(ITypeSymbol left, ITypeSymbol right, Func<uint, bool> parentLookUp, out GeneratorType type, out CodeBuilder? initBuilder, out CodeBuilder propsBuilder)
        {
            #region Finder

            if (_buckets == null)
                Initialize(0);

            int leftId = _Type.GetHashCode(left),
                rightId = _Type.GetHashCode(right);

            uint id = GetMappingId(leftId, rightId),
                index = 0u;

            ref int bucket = ref GetBucket(id);
            int lastIndex = bucket - 1;

            TypeMapping entry = default;

            _Type? leftExistent = null, rightExistent = null;

            while ((uint)lastIndex < (uint)_entries.Length)
            {
                entry = _entries[lastIndex];

                if (_uintComparer.Equals(entry.Id, id))
                    return entry.TryGetMapper(leftId, rightId, out type, out initBuilder, out propsBuilder);

                if (leftExistent is null)
                {
                    if (entry.leftId == leftId)
                        leftExistent = entry.LeftType;
                    else if (entry.rightId == rightId)
                        leftExistent = entry.RightType;
                }

                if (rightExistent is null)
                {
                    if (entry.leftId == leftId)
                        rightExistent = entry.LeftType;
                    else if (entry.rightId == rightId)
                        rightExistent = entry.RightType;
                }

                lastIndex = entry.next;

                if (++index > (uint)_entries.Length)
                    throw new NotSupportedException("Concurrent operations are not allowed");
            }

            int num4 = _freeCount > 0 ? _freeList : (int)_count;

            if (_freeCount > 0)
            {
                _freeList = -3 - _entries[_freeList].next;
                _freeCount--;
            }
            else if (_count == _entries.Length)
            {
                Resize();
                bucket = ref GetBucket(id);
            }

            _count++;

            entry = _entries[num4] = new(id, this, leftExistent ?? new(left), rightExistent ?? new(right), bucket - 1, newId => id == newId || parentLookUp(newId), out var canMap);

            type = entry.GeneratorType;

            bucket = num4 + 1;

            _version++;

            #endregion

            initBuilder = propsBuilder = null!;

            return canMap && entry.TryGetMapper(leftId, rightId, out type, out initBuilder, out propsBuilder)!;
        }

        internal static uint GetMappingId(int leftId, int rightId)
        {
            return (uint)(Math.Min(leftId, rightId), Math.Max(leftId, rightId)).GetHashCode();
        }

        internal static uint GetMappingId(_Type left, _Type right)
        {
            return (uint)(Math.Min(left.Id, right.Id), Math.Max(left.Id, right.Id)).GetHashCode();
        }

        public TypeMapping GetOrAddMapper(ITypeSymbol left, ITypeSymbol right, Func<uint, bool> parentLookUp, out bool canMap)
        {
            #region Finder

            if (_buckets == null)
                Initialize(0);

            int leftId = _Type.GetHashCode(left),
                rightId = _Type.GetHashCode(right);

            //Essential for sorted insertion
            uint id = (uint)(Math.Min(leftId, rightId), Math.Max(leftId, rightId)).GetHashCode(),
                index = 0u;

            ref int bucket = ref GetBucket(id);
            int lastIndex = bucket - 1;

            _Type? leftExistent = null, rightExistent = null;

            while ((uint)lastIndex < (uint)_entries.Length)
            {
                ref TypeMapping entry = ref _entries[lastIndex];

                if (canMap = _uintComparer.Equals(entry.Id, id))
                    return entry;

                lastIndex = entry.next;

                if (++index > (uint)_entries.Length)
                    throw new NotSupportedException("Concurrent operations are not allowed");


                if (leftExistent is null)
                {
                    if (entry.leftId == leftId)
                        leftExistent = entry.LeftType;
                    else if (entry.rightId == rightId)
                        leftExistent = entry.RightType;
                }

                if (rightExistent is null)
                {
                    if (entry.leftId == leftId)
                        rightExistent = entry.LeftType;
                    else if (entry.rightId == rightId)
                        rightExistent = entry.RightType;
                }
            }

            int num4 = _freeCount > 0 ? _freeList : (int)_count;

            if (_freeCount > 0)
            {
                _freeList = -3 - _entries[_freeList].next;
                _freeCount--;
            }
            else if (_count == _entries.Length)
            {
                Resize();
                bucket = ref GetBucket(id);
            }

            _count++;

            var entry2 = _entries[num4] = new(id, this, leftExistent ?? new(left), rightExistent ?? new(right), bucket - 1, nId => nId == id || parentLookUp(nId), out canMap);

            bucket = num4 + 1;

            _version++;

            #endregion

            return entry2;
        }

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

        private uint Initialize(uint capacity)
        {
            uint prime = GetPrime(capacity);
            int[] buckets = new int[prime];
            TypeMapping[] entries = new TypeMapping[prime];
            _freeList = -1;
            _fastModMultiplier = GetFastModMultiplier((uint)prime);
            _buckets = buckets;
            _entries = entries;
            return prime;
        }

        private static ulong GetFastModMultiplier(uint divisor)
        {
            return ulong.MaxValue / divisor + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref int GetBucket(uint hashCode)
        {
            int[] buckets = _buckets;
            return ref buckets[FastMod(hashCode, (uint)buckets.Length, _fastModMultiplier)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint FastMod(uint value, uint divisor, ulong multiplier)
        {
            return (uint)(((multiplier * value >> 32) + 1) * divisor >> 32);
        }

        private void Resize()
        {
            Resize(ExpandPrime(_count));
        }

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

        internal ISymbol? GetSymbol(SyntaxNode id)
        {
            return _compilation.GetSemanticModel(id.SyntaxTree).GetSymbolInfo(id).Symbol;
        }

        internal Enumerator GetEnumerator(uint parentId, ImmutableArray<ISymbol> leftMembers, ImmutableArray<ISymbol> rightMembers) => 
            new (this, parentId, leftMembers, rightMembers);

        internal struct Enumerator(SymbolsMap map, uint parentId, ImmutableArray<ISymbol> leftMembers, ImmutableArray<ISymbol> rightMembers)
        {
            int l = -1, r = -1;
            readonly int lLen = leftMembers.Length, rLen = rightMembers.Length;

            public bool MoveNext(out MemberMapping mapping)
            {
                while (--l < lLen)
                {
                    if (IsNotMappableMember(leftMembers[l], out var leftType, out var leftMemberInfo))
                        continue;

                    while (--r < rLen)
                    {
                        if (IsNotMappableMember(rightMembers[r], out var rightType, out var rightMemberInfo)) 
                            continue;

                        var typeMapping = map.GetOrAddMapper(leftType, rightType, null!, out bool exists);

                        if (exists)
                        {
                            mapping = new(parentId, map, leftMemberInfo, rightMemberInfo, typeMapping);
                            return true;
                        }
                        mapping = default;
                        return false;
                    }
                    r = -1;
                }
                mapping = default;
                return false;
            }


            private static bool IsNotMappableMember(ISymbol member, out ITypeSymbol typeOut, out Member memberOut)
            {
                typeOut = default!;
                return (memberOut = member switch
                {
                    IPropertySymbol
                    {
                        ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                        IsIndexer: false,
                        Type: { } type,
                        DeclaredAccessibility: var accessibility,
                        IsReadOnly: var isReadonly,
                        IsWriteOnly: var isWriteOnly
                    }
                        => new(
                            _Type.GetHashCode(member),
                            accessibility is Accessibility.Internal or Accessibility.Public && !isWriteOnly,
                            accessibility is Accessibility.Internal or Accessibility.Public && !isReadonly,
                            member.ToNameOnly(),
                            typeOut = type,
                            member.GetAttributes()),
                    IFieldSymbol
                    {
                        ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                        Type: { } type,
                        DeclaredAccessibility: var accessibility,
                        IsReadOnly: var isReadonly
                    }
                        => new(
                            _Type.GetHashCode(member),
                            accessibility is Accessibility.Internal or Accessibility.Public,
                            accessibility is Accessibility.Internal or Accessibility.Public && !isReadonly,
                            member.ToNameOnly(),
                            typeOut = type,
                            member.GetAttributes()),
                    _ => default
                }) is ({ Exists: false});
            }

            public void Reset()
            {
                l = r = 0;
            }
        }

    } 
    internal enum GeneratorType
    {
        Simple,
        Enumerable,
        None,
        Complex
    }
}


/*
internal delegate void CodeBuilder(CodeBuilderArgs args, Indent indent, ref string? comma);

internal sealed class CodeBuilderArgs(string to, string from, int parentScope)
{
    public readonly string to = to;
    public readonly string from = from;
    public readonly int parentScope = parentScope;
}

internal enum IterableType { Collection, Enumerable, Array }

internal class SymbolsMap
{

    readonly StringBuilder code = new(@"namespace SourceCrafter.Mappings;

public static partial class Mappers
{");

    internal int _count = 0;

    internal Binding[] _entries = { };

    private int[] _buckets = null!;
    private static readonly int[] s_primes = new int[72]
    {
        3, 7, 11, 17, 23, 29, 37, 47, 59, 71,
        89, 107, 131, 163, 197, 239, 293, 353, 431, 521,
        631, 761, 919, 1103, 1327, 1597, 1931, 2333, 2801, 3371,
        4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519, 21023,
        25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363,
        156437, 187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403,
        968897, 1162687, 1395263, 1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559,
        5999471, 7199369
    };


    private int _freeList;
    private ulong _fastModMultiplier;
    private int _version, _freeCount;

    internal static SymbolEqualityComparer _comparer = SymbolEqualityComparer.Default;
    static EqualityComparer<uint> _uintComparer = EqualityComparer<uint>.Default;

    public bool TryGetOrAddMapper(Compilation compilation, ITypeSymbol toInfo, ITypeSymbol fromInfo, out CodeBuilder? itersMapBuilder, out CodeBuilder mapBuilder)
    {
        #region Finder

        if (_buckets == null)
            Initialize(0);

        int toId = _comparer.GetHashCode(toInfo),
            fromId = _comparer.GetHashCode(fromInfo);

        uint id = (uint)(Math.Min(toId, fromId), Math.Max(toId, fromId)).GetHashCode(),
            index = 0u;

        ref int bucket = ref GetBucket(id);
        int lastIndex = bucket - 1;

        while ((uint)lastIndex < (uint)_entries.Length)
        {
            ref var entry = ref _entries[lastIndex];

            if (_uintComparer.Equals(entry.id, id))
            {
                return entry.TryGetMapper(fromId, toId, out itersMapBuilder, out mapBuilder)!;
            }

            lastIndex = entry.next;

            if (++index > (uint)_entries.Length)
                throw new NotSupportedException("Concurrent operations are not allowed");
        }

        int num4 = _freeCount > 0 ? _freeList : _count;

        if (_freeCount > 0)
        {
            _freeList = -3 - _entries[_freeList].next;
            _freeCount--;
        }
        else if (_count == _entries.Length)
        {
            Resize();
            bucket = ref GetBucket(id);
        }

        _count++;

        ref Binding newEntry = ref _entries[num4];

        newEntry.next = bucket - 1;

        newEntry.id = id;
        newEntry.toId = toId;
        newEntry.fromId = fromId;

        bucket = num4 + 1;

        _version++;

        #endregion

        CreateMappers(compilation, toInfo, fromInfo, ref newEntry);

        return newEntry.TryGetMapper(fromId, toId, out itersMapBuilder, out mapBuilder)!;
    }

    public Binding GetOrAddMapper(Compilation compilation, ITypeSymbol toInfo, ITypeSymbol fromInfo)
    {
        #region Finder

        if (_buckets == null)
            Initialize(0);

        int toId = _comparer.GetHashCode(toInfo),
            fromId = _comparer.GetHashCode(fromInfo);

        //Essential for sorted insertion
        uint id = (uint)(Math.Min(toId, fromId), Math.Max(toId, fromId)).GetHashCode(),
            index = 0u;

        ref int bucket = ref GetBucket(id);
        int lastIndex = bucket - 1;

        Binding entry;

        while ((uint)lastIndex < (uint)_entries.Length)
        {
            entry = _entries[lastIndex];

            if (_uintComparer.Equals(entry.id, id))
            {
                return entry;
            }

            lastIndex = entry.next;

            if (++index > (uint)_entries.Length)
                throw new NotSupportedException("Concurrent operations are not allowed");
        }

        int num4 = _freeCount > 0 ? _freeList : _count;

        if (_freeCount > 0)
        {
            _freeList = -3 - _entries[_freeList].next;
            _freeCount--;
        }
        else if (_count == _entries.Length)
        {
            Resize();
            bucket = ref GetBucket(id);
        }

        _count++;

        entry = _entries[num4];

        entry.next = bucket - 1;

        entry.id = id;
        entry.toId = toId;
        entry.fromId = fromId;

        bucket = num4 + 1;

        _version++;

        #endregion

        CreateMappers(compilation, toInfo, fromInfo, ref entry);

        return entry;
    }

    private void CreateMappers(Compilation compilation, ITypeSymbol left, ITypeSymbol right, ref Binding newEntry)
    {
        CodeBuilder?
            initializers = null,
            propsBuilder = null,
            reverseIteratorBuilder = null,
            reversePropsBuilder = null;

        var isLeftNullable = left.IsNullable();

        var leftId = newEntry.toId;

        var leftTypeName = left.ToGlobalizedNamespace();
        var leftMembersEnumerator = left.GetMembers().GetEnumerator();

        while (leftMembersEnumerator.MoveNext())
        {
            if (IsNotMappableMember(leftMembersEnumerator.Current, out var leftMemberInfo)) continue;

            var leftMember = leftMembersEnumerator.Current;

            var rightMembersEnumerator = right.GetMembers().GetEnumerator();

            while (rightMembersEnumerator.MoveNext())
            {
                //Should discard property if not matching requirements
                if (IsNotMappableMember(rightMembersEnumerator.Current, out var rightMemberInfo)) continue;

                var rightMember = rightMembersEnumerator.Current;

                var (leftMemberName, rightMemberName) = (leftMember.ToNameOnly(), rightMember.ToNameOnly());

                newEntry.AreNameEquals = leftMemberName == rightMemberName;

                //If not equal names and not mappable by atribute
                if (!CanMapByDesignAndAttribute(nameEquals, compilation, leftMemberInfo, rightMemberInfo, rightMember))
                {
                    if (!CanMapByDesignAndAttribute(nameEquals, compilation, rightMemberInfo, leftMemberInfo, leftMember))
                        continue;

                    goto reverse;
                }

                #region TO - FROM

                GetRequiredMarks(leftMemberInfo, rightMemberInfo, out string required, out string defaultRequired);

                //Based on conversion
                if (HasConversion(leftMemberInfo.Type, rightMemberInfo.Type, out var hasImplicitConversion, out var isReference, compilation))
                {
                    newEntry.hasMapping |= true;

                    propsBuilder += CreateConversionAssignment(isReference, leftMemberName, rightMemberName, rightMemberInfo.TypeName, required);
                }
                //Single assignment for implicit conversions
                else if (hasImplicitConversion)
                {
                    newEntry.hasMapping |= true;

                    propsBuilder += CreateSimpleAssignment(leftMemberName, rightMemberName, rightMemberInfo.IsNullable, required, defaultRequired);
                }
                else if (_comparer.Equals(leftMemberInfo.Type, left) && _comparer.Equals(rightMemberInfo.Type, right))
                {
                    newEntry.hasMapping |= true;

                    propsBuilder += (CodeBuilderArgs args, Indent indent, ref string? comma) =>
                    {
                        code.AppendFormat(@"{0}
{1}    {2} = ", Interlocked.Exchange(ref comma, ","), indent, rightMemberName);

                        comma ??= ",";

                        if (leftMemberInfo.ReachedThreshold(leftId))
                        {
                            code.AppendFormat("default{0}", defaultRequired);
                        }
                        else
                        {
                            indent++;
                            StartNullableType(leftTypeName, args.to + leftMemberName, args.from + '.' + rightMemberName, ref comma, ref indent);

                            propsBuilder?.Invoke(new(args.to + leftMemberName, args.from + rightMemberName, leftMemberInfo.Id), indent, ref comma);

                            EndNullableType(indent, defaultRequired);
                            indent--;
                        }
                    };
                }
                else if (leftMemberInfo.IsEnumerable && rightMemberInfo.IsEnumerable)
                {
                    AddEnumerableMapping(compilation, leftMemberInfo, rightMemberInfo, newEntry.toId);
                }

                #endregion

                if (!CanMapByDesignAndAttribute(nameEquals, compilation, rightMemberInfo, leftMemberInfo, leftMember))
                    break;

                reverse:

                #region FROM - TO

                #endregion

                break;
            }
        }

        newEntry.propsBuilder = BuildType(initializers, propsBuilder!, isLeftNullable, leftTypeName);

        static bool IsNotMappableMember(ISymbol symbol, out SymbolInfo symbolInfo) =>
            (symbolInfo = symbol switch
            {
                IPropertySymbol
                {
                    ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                    IsIndexer: false,
                    Type: { } type,
                    DeclaredAccessibility: var accessibility,
                    IsReadOnly: var isReadonly,
                    IsWriteOnly: var isWriteOnly
                }
                    => new(_comparer.GetHashCode(symbol)
,
                           accessibility,
                           isReadonly,
                           true,
                           type,
                           isWriteOnly,
                           symbol.GetAttributes()),
                IFieldSymbol
                {
                    ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                    Type: { } type,
                    DeclaredAccessibility: var accessibility,
                    IsReadOnly: var isReadonly
                }
                    => new(_comparer.GetHashCode(symbol)
,
                           accessibility,
                           isReadonly,
                           true,
                           type,
                           false,
                           symbol.GetAttributes()),
                _ => default!
            })?.IsMappable == false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CodeBuilder BuildType(CodeBuilder? initializers, CodeBuilder propsBuilder, bool isLeftNullable, string leftTypeName, string defaultRequired)
    {
        return isLeftNullable
        // Build nullable inline mapper
        ? new CodeBuilder((CodeBuilderArgs args, Indent indent, ref string? comma) =>
        {
            StartNullableType(leftTypeName, args, ref comma, ref indent);

            propsBuilder(args, indent, ref comma);

            EndNullableType(indent, defaultRequired);
        })
        : new CodeBuilder((CodeBuilderArgs args, Indent indent, ref string? comma) =>
        {
            StartType(leftTypeName, ref comma, ref indent);

            propsBuilder(args.to, args.from, indent, ref comma);

            EndType(indent);
        });
    }

    private static bool HasConversion(ITypeSymbol fromType, ITypeSymbol toType, out bool hasImplicitConversion, out bool isReference, Compilation compilation)
    {
        var conv = compilation.ClassifyConversion(fromType, toType);
        isReference = conv.IsReference;
        hasImplicitConversion = conv.Exists && conv.IsImplicit;
        return conv.Exists && conv.IsExplicit;
    }

    private CodeBuilder CreateConversionAssignment(bool isReference, string leftMemberName, string rightMemberName, string rightMemberType, string required)
    {
        return isReference
            //If is by ref
            ? (CodeBuilderArgs args, Indent indent, ref string? comma)
                => code.AppendFormat(@"{0}
{1}    {2} = {3}.{4} as {5}", Interlocked.Exchange(ref comma, ","), indent, leftMemberName, from, rightMemberName, rightMemberType.TrimEnd('?'))
            //Is by value
            : (CodeBuilderArgs args, Indent indent, ref string? comma)
                => code.AppendFormat(@"{0}
{1}    {2} = ({3}){4}.{5}{6}", Interlocked.Exchange(ref comma, ","), indent, leftMemberName, rightMemberType, from, rightMemberName, required);

    }

    private void EndType(Indent indent)
    {
        indent--;

        code.AppendFormat(@"
{0}    }}", indent);
    }

    private void StartType(string toTypeName, ref string? comma, ref Indent indent)
    {
        code.AppendFormat(@"new {0}()
{1}    {{", toTypeName.Replace("?", ""), indent);

        comma = null;
        indent++;
    }

    private void EndNullableType(Indent indent, string defaultRequired)
    {
        indent--;

        code.AppendFormat(@"
{0}    }}
{0}    : default{1}", indent, defaultRequired);
    }

    private void StartNullableType(string typeName, string left, string right, ref string? comma, ref Indent indent)
    {
        code.AppendFormat(@"{0} is {{}} {1} 
{2}    ? new {3}()
{2}    {{", left, right, indent, typeName.Replace("?", ""));

        comma = null;
        indent++;
    }

    private CodeBuilder CreateSimpleAssignment(string leftMemberName,, string rightMemberName, bool isRightNullable, string required, string defaultRequired)
    {
        return isRightNullable
                //build raw assignation
                ? (CodeBuilderArgs args, Indent indent, ref string? comma) =>
            code.AppendFormat(@"{0}
{1}    {2} = {3}.{4} is {{}} {3}{4} ? {3}{4} : default{5}", Interlocked.Exchange(ref comma, ","), indent, leftMemberName, right, rightMemberName, defaultRequired)

                : (CodeBuilderArgs args, Indent indent, ref string? comma) =>
            code.AppendFormat(@"{0}
{1}    {2} = {3}.{4}{5}", Interlocked.Exchange(ref comma, ","), indent, leftMemberName, right, rightMemberName, required);
    }

    private static bool CanMapByDesignAndAttribute(bool isMappable, Compilation compilation, SymbolInfo left, SymbolInfo right, ISymbol fromMember)
    {
        if (!left.IsWritable || !right.IsReadable)
            return false;

        if (isMappable)
            return true;

        var match = false;

        foreach (var attr in left.Attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() is not { } className) continue;

            if (className == "SourceCrafter.Mapping.Attributes.IgnoreAttribute"
                && (left.IgnoresSource = (Ignore)(int)attr.ConstructorArguments[0].Value!) != Ignore.This) return false;

            if (className != "SourceCrafter.Mapping.Attributes.ThresholdAttribute")
            {
                left.Threshold = (int)attr.ConstructorArguments[1].Value!;
                continue;
            }

            if (className != "SourceCrafter.Mapping.Attributes.MapAttribute") continue;

            if ((left.IgnoresSource = (Ignore)(int)attr.ConstructorArguments[1].Value!) is not (Ignore.This or Ignore.None))
                return false;

            if ((attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0].Expression
                is not InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                }
                || compilation.GetSemanticModel(id.SyntaxTree)?.GetSymbolInfo(id).Symbol is not ISymbol member) continue;

            if (_comparer.Equals(member, fromMember))
                match = true;
        }

        return match;
    }

    private static void GetRequiredMarks(SymbolInfo left, SymbolInfo right, out string required, out string defaultRequired)
    {
        required = !left.IsNullable && right.IsNullable ? "!" : "";
        defaultRequired = !left.IsNullable && (right.AllowsNull || right.IsNullable) ? "!" : "";
    }

    private static int GetPrime(int min)
    {
        int[] array = s_primes;
        foreach (int num in array)
        {
            if (num >= min)
            {
                return num;
            }
        }
        for (int j = min | 1; j < int.MaxValue; j += 2)
        {
            if (IsPrime(j) && (j - 1) % 101 != 0)
            {
                return j;
            }
        }
        return min;
    }

    private static bool IsPrime(int candidate)
    {
        if (((uint)candidate & (true ? 1u : 0u)) != 0)
        {
            int num = (int)Math.Sqrt(candidate);
            for (int i = 3; i <= num; i += 2)
            {
                if (candidate % i == 0)
                {
                    return false;
                }
            }
            return true;
        }
        return candidate == 2;
    }

    private int Initialize(int capacity)
    {
        int prime = GetPrime(capacity);
        int[] buckets = new int[prime];
        Binding[] entries = new Binding[prime];
        _freeList = -1;
        _fastModMultiplier = GetFastModMultiplier((uint)prime);
        _buckets = buckets;
        _entries = entries;
        return prime;
    }

    private static ulong GetFastModMultiplier(uint divisor)
    {
        return ulong.MaxValue / divisor + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetBucket(uint hashCode)
    {
        int[] buckets = _buckets;
        return ref buckets[FastMod(hashCode, (uint)buckets.Length, _fastModMultiplier)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FastMod(uint value, uint divisor, ulong multiplier)
    {
        return (uint)(((multiplier * value >> 32) + 1) * divisor >> 32);
    }

    private void Resize()
    {
        Resize(ExpandPrime(_count), forceNewHashCodes: false);
    }

    private void Resize(int newSize, bool forceNewHashCodes)
    {
        Binding[] array = new Binding[newSize];
        Array.Copy(_entries, array, _count);
        _buckets = new int[newSize];
        _fastModMultiplier = GetFastModMultiplier((uint)newSize);
        for (int j = 0; j < _count; j++)
        {
            if (array[j].next >= -1)
            {
                ref int bucket = ref GetBucket((uint)array[j].id);
                array[j].next = bucket - 1;
                bucket = j + 1;
            }
        }
        _entries = array;
    }

    private static int ExpandPrime(int oldSize)
    {
        int num = 2 * oldSize;
        if ((uint)num > 2147483587u && 2147483587 > oldSize)
        {
            return 2147483587;
        }
        return GetPrime(num);
    }


    private void AddEnumerableMapping(Compilation compilation, SymbolInfo left, SymbolInfo right, int parentId)
    {
        bool hasMapping = false;
        CodeBuilder initializer, propsMapper = null!;

        string
            rightCollVarName = right.MemberName,
            leftCollVarName = left.MemberName,
            rightItemVarName = $"{rightCollVarName}Item",
            leftItemVarName = $"{leftCollVarName}Item";

        CodeBuilder iteratorBuilder = null!;

        switch ((right.IterableType, left.IterableType))
        {
            case (not IterableType.Enumerable, not IterableType.Collection):
                #region Countable to array

                string
                    rightCollItemVarName = $"{rightCollVarName}[{rightCollVarName}Ix]",
                    leftCollItemVarName = $"{leftCollVarName}[{rightCollVarName}Ix]";

                if (!(hasMapping = TryGetOrAddMapper(compilation, left.ItemType, right.ItemType, out initializer, out propsMapper)))
                    return;

                iteratorBuilder = (CodeBuilderArgs args, string left, string right, ref string? _) =>
                {
                    code.AppendFormat(@"
{0}    #region Translating from {1}.{2} to {3}.{4}

{0}    var {5} = {1}.{2};
{0}    var {6} = new {7}[{5}.{8}];

{0}    for(int {5}Ix = 0, {5}Len = {5}.{8}; {5}Ix < {5}Len; {5}Ix++) 
{0}    {{", indent, from, rightCollVarName, to, rightCollVarName, leftCollItemVarName, rightCollItemVarName, right.ItemTypeName, left.CountProperty);

                    indent++;
                };

                if (hasMapping)
                {
                    if (right.IsNullable)
                    {
                        iteratorBuilder += (CodeBuilderArgs args, Indent indent, ref string? comma, int _) =>
                        {
                            initializer?.Invoke(to, from, indent, ref comma, _);

                            code.AppendFormat(@"{0}
{1}    {2} = ", comma, indent, leftCollItemVarName);

                            BuildType(initializers, propsMapper, indent, ref comma, _);



                        };
                        //                        iteratorBuilder += (CodeBuilderArgs args, Indent indent, ref string? _) =>
                        //                        {
                        //                            code.AppendFormat(@"
                        //{0}    if ({1}{2}[] is not {{}} {1}{2}Item) 
                        //{0}    {{
                        //{0}        {3} = default;
                        //{0}        continue;
                        //{0}    }}
                        //", indent, from, right.MemberName, to, left.MemberName);
                        //                        };
                    }
                    //                    else
                    //                    {
                    //                        iteratorBuilder += (string fto, string from, string to, Indent indent, ref string? _) =>
                    //                        {
                    //                            code.AppendFormat(@"
                    //{0}    var {1} = {2};", indent, inItemVarName, inCollItemVarName);
                    //                        };
                    //                    }

                    //                    iteratorBuilder += (CodeBuilderArgs args, Indent indent, ref string? _) =>
                    //                    {
                    //                        //BuildType(codeGen, indent, (memberId, threshold), right.ItemFullTypeName.TrimEnd('?'), inItemVarName, outItemVarName);
                    //                    };
                }
                else
                {
                    leftItemVarName = rightCollItemVarName;
                }

                iteratorBuilder += (CodeBuilderArgs args, Indent indent, ref string? _) =>
                {
                    indent--;

                    code.AppendFormat(@"
{0}        {1} = {2};
{0}    }}

{0}    {3} = {4};
{0}    #endregion
", indent, leftCollItemVarName, rightCollItemVarName, to + '.' + left.MemberName, rightCollVarName);
                };

                #endregion
                break;
                //            case (_, IterableType.Collection):
                //                #region Any to collection

                //                if (!NeedsConversion(
                //                        compilation,
                //                        left.Type,
                //                        right.Type,
                //                        right.ItemFullTypeName,
                //                        ref outItemVarName,
                //                        out hasMapping,
                //                        out codeGen))

                //                    return;

                //                iteratorBuilder = (from, to, indent, _) =>
                //                {
                //                    AppendFormat(@"
                //{0}    #region Translating from {1} to {2}

                //{0}    var {3} = {1};
                //{0}    var {4} = new {5}();

                //{0}    foreach (var {6} in {3})
                //{0}    {{", indent, inExpression, fromExpression, inCollVarName, outCollVarName, outTypeName.TrimEnd('?'), inItemVarName);


                //                    indent++;
                //                };

                //                if (hasMapping)
                //                {
                //                    if (inNullable)
                //                    {
                //                        iteratorBuilder += (from, to, indent, _) =>
                //                        {
                //                            AppendFormat(@"
                //{0}    if ({1} is null) 
                //{0}    {{
                //{0}        {2}
                //{0}        continue;
                //{0}    }}
                //", indent, inItemVarName, string.Format(right.AddMethod, outCollVarName, "default"));
                //                        };
                //                    }

                //                    iteratorBuilder += (from, to, indent, _) =>
                //                    {
                //                        BuildType(codeGen, indent, memberNode, right.ItemFullTypeName.TrimEnd('?'), inItemVarName, outItemVarName);
                //                    };
                //                }

                //                iteratorBuilder += (from, to, indent, _) =>
                //                {
                //                    indent--;

                //                    AppendFormat(@"
                //{0}        {1}
                //{0}    }}

                //{0}    {2} = {3};

                //{0}    #endregion
                //",
                //                    indent,
                //                    string.Format(right.AddMethod, outCollVarName, outItemVarName),
                //                    fromExpression,
                //                    outCollVarName);
                //                };
                //                #endregion
                //                break;
                //            case (_, IterableType.Enumerable):
                //                #region Any to collection

                //                if (!NeedsConversion(
                //                        compilation,
                //                        left.Type,
                //                        right.Type,
                //                        right.ItemFullTypeName,
                //                        ref outItemVarName,
                //                        out hasMapping,
                //                        out codeGen))

                //                    return;

                //                iteratorBuilder = (from, to, indent, _) =>
                //                {
                //                    AppendFormat(@"
                //{0}    var {1}{2} = {1}.{2};
                //{0}    var {3}{4} = new global::System.Collections.Generic.List<{5}>();

                //{0}    foreach (var {1}{2}Item in {1}{2})
                //{0}    {{", indent, from, inExpression, to, outMemberName, right.ItemFullTypeName);

                //                    indent++;
                //                };

                //                if (hasMapping)
                //                {
                //                    iteratorBuilder += (from, to, indent, _) =>
                //                    {
                //                        BuildType(codeGen, indent++, memberNode, right.ItemFullTypeName.TrimEnd('?'), from + inItemVarName, to + outMemberName + "Item", string.Format(@"
                //{0}{1}.Add({2}", indent, to + outMemberName, inNullable ? string.Format(@"{0} is null 
                //{1}    ? default
                //{1}    : ", from + inItemVarName, indent) : null));

                //                        indent--;
                //                    };
                //                }
                //                else
                //                {
                //                    iteratorBuilder += (from, to, indent, _) =>
                //                    {
                //                        AppendFormat(@"
                //{0}    {1}.Add({2}", indent, to + outMemberName, string.Format(fromExpression, from, inExpression));
                //                    };
                //                }

                //                iteratorBuilder += (from, to, indent, _) =>
                //                {
                //                    indent--;

                //                    AppendFormat(@");
                //{0}    }}

                //{0}    {3} = {4};

                //{0}    #endregion
                //", indent, outCollVarName, outItemVarName, fromExpression, outCollVarName);
                //                };
                //                #endregion
                //                break;
                //            case (_, IterableType.Array or IterableType.Enumerable):
                //                #region Any to array

                //                inCollItemVarName = $"{inCollVarName}[{inCollVarName}Ix]";
                //                outCollItemVarName = $"{outCollVarName}[{inCollVarName}Ix]";

                //                if (!NeedsConversion(
                //                    compilation,
                //                    left.Type,
                //                    right.Type,
                //                    right.ItemFullTypeName,
                //                    ref outItemVarName,
                //                    out hasMapping,
                //                    out codeGen))

                //                    return;

                //                iteratorBuilder = (from, to, indent, _) =>
                //                {
                //                    AppendFormat(@"
                //{0}    #region Translating from {1} to {2}

                //{0}    var {3} = {1};
                //{0}    var {4} = new {5}[4];
                //{0}    var {4}Count = 0;
                //{0}    var {3}Ix = 0;

                //{0}    foreach (var {6} in {3})
                //{0}    {{
                //{0}        if ({4}Count == {4}.Length) 
                //{0}            global::System.Array.Resize(ref {4}, {4}.Length * 2);
                //", indent, inExpression, fromExpression, inCollVarName, outCollVarName, right.ItemFullTypeName, inItemVarName);

                //                    indent++;
                //                };

                //                if (hasMapping)
                //                {
                //                    if (inNullable)
                //                    {
                //                        iteratorBuilder += (from, to, indent, _) =>
                //                        {
                //                            AppendFormat(@"
                //{0}    if ({1} is not {{}} {2}) 
                //{0}    {{
                //{0}        {3} = default;
                //{0}        continue;
                //{0}    }}
                //", indent, inCollItemVarName, inItemVarName, outCollItemVarName);
                //                        };
                //                    }
                //                    else
                //                    {
                //                        iteratorBuilder += (from, to, indent, _) =>
                //                        {
                //                            AppendFormat(@"
                //{0}    var {1} = {2};", indent, inItemVarName, inCollItemVarName);
                //                        };
                //                    }

                //                    iteratorBuilder += (from, to, indent, _) =>
                //                    {
                //                        BuildType(codeGen, indent, memberNode, right.ItemFullTypeName.TrimEnd('?'), inItemVarName, outItemVarName);
                //                    };
                //                }
                //                else
                //                {
                //                    outItemVarName = inCollItemVarName;
                //                }

                //                iteratorBuilder += (from, to, indent, _) =>
                //                {
                //                    indent--;

                //                    AppendFormat(@"
                //{0}        {1}[{2}Ix++] = {3};
                //{0}    }}

                //{0}    if ({1}Count < {1}.Length) 
                //{0}        global::System.Array.Resize(ref {1}, {1}Count);

                //{0}    {4} = {1};

                //{0}    #endregion
                //", indent, outCollVarName, inCollVarName, outItemVarName, fromExpression);
                //                };
                //                #endregion
                //                break;
        }

        //if (iteratorBuilder != null)
        //{
        //    parentCodeGen.InitializeIterables += (from, to, indent, parentMember) =>
        //    {
        //        if (parentMember.AddChild(memberNode))
        //        {
        //            iteratorBuilder(from, to, indent, memberNode);
        //        }
        //    };
        //}

    }


    private static string SanitizeMethodName(ITypeSymbol type)
    {
        string typeName = type.ToTypeNameFormat();

        switch (type)
        {
            case INamedTypeSymbol { IsGenericType: true }:
                typeName = typeName.Replace("<", "Of").Replace(">", "_").Replace(",", "And").Replace(" ", "").TrimEnd('_', '?');
                break;
            case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:
                typeName = "TupleOf" + string.Join("And", els.Select(f => f.IsExplicitlyNamedTupleElement ? f.Name : SanitizeMethodName(f.Type)));
                break;
        }

        return typeName;
    }

    private string GenerateMethodName(string typeName, string typeName2, ITypeSymbol type)
    {
        for (
            ISymbol ns = type.ContainingNamespace;
            typeName == typeName2 && ns != null;
            typeName = ns.ToString() + typeName, ns = type.ContainingNamespace
        ) ;

        return "To" + (typeName.StartsWith(typeName2) ? typeName[typeName2.Length..] : typeName);
    }

    internal static readonly Func<ISymbol?, ISymbol?, bool> AreSymbolsEquals = SymbolEqualityComparer.Default.Equals;
}

readonly struct ItemSymbolInfo
{
    internal readonly int Id;

    internal ItemSymbolInfo(string? addMethod, string countProperty, ITypeSymbol type, IterableType iterableType)
    {
        Id = SymbolsMap._comparer.GetHashCode(type);
    }
}

sealed class SymbolInfo
{
#pragma warning disable CS8618 // Un campo que no acepta valores NULL debe contener un valor distinto de NULL al salir del constructor. Considere la posibilidad de declararlo como que admite un valor NULL.

    public SymbolInfo(int id, Accessibility accessibility, bool isReadOnly, bool isMappable, ITypeSymbol type, bool isWriteOnly, ImmutableArray<AttributeData> attributes)
    {
        Id = id;
        IsWritable = accessibility is Accessibility.Public or Accessibility.Internal && !isReadOnly;
        IsReadable = accessibility is Accessibility.Public or Accessibility.Internal && !isWriteOnly;
        IsMappable = isMappable;

        Type = type;

        if (IsEnumerable = IsEnumerableType(type, out ItemType, out AddMethod, out CountProperty, out IterableType))
        {
            ItemTypeName = ItemType.ToGlobalizedNamespace();
            Attributes = ItemType.GetAttributes();
        }

        IsNullable = type.IsNullable();
        AllowsNull = type.AllowsNull();
        NonGenericTypeName = type.ToGlobalizedNonGenericNamespace();
        TypeName = type.ToGlobalizedNamespace();

        Attributes = attributes;
    }

#pragma warning restore CS8618 // Un campo que no acepta valores NULL debe contener un valor distinto de NULL al salir del constructor. Considere la posibilidad de declararlo como que admite un valor NULL.

    internal static ITypeSymbol objectTypeSymbol = null!;
    internal readonly ITypeSymbol Type, ItemType;
    internal readonly int Id;
    internal readonly string MemberName, TypeName, NonGenericTypeName, ItemTypeName, AddMethod, CountProperty;
    internal readonly ImmutableArray<AttributeData> Attributes, ItemTypeAttributes;
    internal readonly bool IsNullable, AllowsNull, IsMappable, IsEnumerable, NotPrimitive, IsReadable, IsWritable;
    internal int Threshold;
    internal Ignore IgnoresSource;
    internal readonly IterableType IterableType;

    internal readonly SymbolInfo? Parent;

    bool IsEnumerableType(ITypeSymbol type, out ITypeSymbol itemType, out string addMethod, out string countProperty, out IterableType iterType)
    {
        itemType = default!;
        addMethod = default!;
        countProperty = default!;
        iterType = default;

        if (type.SpecialType == SpecialType.System_String || type.IsPrimitive())
            return false;

        switch (NonGenericTypeName)
        {
            case "global::System.Collections.Generic.Stack"
            :
                addMethod = "{0}.Push({1});";
                countProperty = "Count";
                itemType = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol;

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                addMethod = "{0}.Enqueue({1});";
                countProperty = "Count";
                itemType = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol;

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                addMethod = "{0}.Add({1});";
                countProperty = "Count";
                itemType = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol;

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                countProperty = "Count";
                itemType = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol;

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                itemType = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol;
                iterType = IterableType.Enumerable;

                return true;

            default:
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                {

                    countProperty = "Length";
                    itemType = elType;
                    iterType = IterableType.Array;


                    return true;
                }
                else
                    foreach (var item in type.AllInterfaces)
                        if (IsEnumerableType(item, out itemType, out addMethod, out countProperty, out iterType))
                            return true;
                break;
        }

        return false;
    }

    internal uint GetHashCode(SymbolInfo other) =>
        (uint)(Math.Min(Id, other.Id), Math.Max(Id, other.Id)).GetHashCode();

    internal string Stringify()
    {
        var parent = Parent?.Stringify() ?? "from";
        if (string.IsNullOrEmpty(MemberName))
            return parent;
        return parent + (Parent?.IsNullable == true ? "?." : ".") + MemberName;
    }

    Dictionary<int, int> threshold = new();

    internal bool ReachedThreshold(int scope)
    {
        if (!threshold.TryGetValue(Id, out int thresholdCount))
        {
            threshold[Id] = thresholdCount = 0;
        }
        return thresholdCount > Threshold;
    }
}

public enum MapType
{
    None,
    NameEquals
}


internal record struct Binding()
{
    public uint id = 0;

    internal int next = 0, toId, fromId;

    internal bool hasMapping = false;
    internal bool hasReverseMapping = false;

    internal readonly bool IsMappable => hasMapping || hasReverseMapping;

    internal CodeBuilder?
        propsBuilder = null,
        itersBuilder = null,
        reversePropsBuilder = null,
        reverseItersBuilder = null;

    internal readonly bool TryGetMapper(int left, int right, out CodeBuilder? iteratorsBuilder, out CodeBuilder propsBuilder)
    {
        if ((toId, fromId) == (left, right))
        {
            propsBuilder = this.propsBuilder!;
            iteratorsBuilder = itersBuilder;
            return hasMapping;
        }
        else
        {
            propsBuilder = reversePropsBuilder!;
            iteratorsBuilder = reverseItersBuilder;
            return hasReverseMapping;
        }
    }

    internal int _recursiveCount = 0;

    private int _threshold = 2;

    internal bool ThresholdReached
    {
        get
        {
            if (_recursiveCount + 1 < _threshold)
            {
                _recursiveCount++;
                return true;
            }
            return false;
        }
    }


}


 */