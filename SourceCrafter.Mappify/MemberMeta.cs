using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Helpers;

namespace SourceCrafter.Mappify;

internal class MemberMeta(
    int id,
    string name,
    TypeMeta type,
    TypeMeta? owningType = null,
    bool isNullable = false,
    HashSet<int>? manualMatches = null,
    HashSet<int>? ignoreFor = null,
    bool canRead = true,
    bool canWrite = true,
    bool useUnsafeAccessor = false,
    short maxDepth = 1,
    string privateFieldMethodName = "")
{
    private readonly int _id = id;

    public readonly int HashCode = (type.Id, name).GetHashCode();

    internal readonly string Name = name;
    internal readonly TypeMeta Type = type;
    // internal readonly bool IsNullable;

    internal readonly TypeMeta? OwningType = owningType;

    private readonly HashSet<int> _ignores = ignoreFor ?? [], _matches = manualMatches ?? [];
    private readonly bool _canRead = canRead, _canWrite = canWrite;
    internal readonly bool IsNullable = isNullable, UseUnsafeAccessor = useUnsafeAccessor;

    internal readonly short MaxDepth = maxDepth;

    internal readonly string UnsafeFieldAccesor = privateFieldMethodName;

    internal bool Discard(in MemberMeta target, bool ignoreCase, out MemberContext targetCtx, out MemberContext sourceCtx)
    {
        sourceCtx = targetCtx = default;

        if (_id != target._id
            && !Name.Equals(target.Name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && !target.Name.Equals(Type.Name + Name)
            && !Name.Equals(target.Type.Name + target.Name)
            && !target._matches.Contains(_id)
            && !_matches.Contains(target._id))
        {
            return true;
        }

        targetCtx = new(
            target,
            this,
            _ignores.Contains(target._id) && !_matches.Contains(target._id),
            GetDefaultBangChar(IsNullable, target.IsNullable, target.Type.Symbol.AllowsNull()),
            GetBangChar(IsNullable, target.IsNullable));

        sourceCtx = new(
            this,
            target,
            target._ignores.Contains(_id) && !_matches.Contains(target._id),
            GetDefaultBangChar(target.IsNullable, IsNullable, Type.Symbol.AllowsNull()),
            GetBangChar(target.IsNullable, IsNullable));

        return !target._canWrite
               || !_canRead
               || !_canWrite
               || !target._canRead;
    }

    private static string? GetDefaultBangChar(bool isTargetNullable, bool isSourceNullable, bool sourceAllowsNull)
        => !isTargetNullable && (isSourceNullable || sourceAllowsNull) ? "!" : null;

    private static string? GetBangChar(bool isTargetNullable, bool isSourceNullable)
        => !isTargetNullable && isSourceNullable ? "!" : null;



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(in MemberMeta a, in MemberMeta b)
    {
        return (a.Type.Id, a.Name) == (b.Type.Id, b.Name);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(in MemberMeta a, in MemberMeta b)
    {
        return (a.Type.Id, a.Name) != (b.Type.Id, b.Name);
    }

    public override bool Equals(object? obj)
    {
        return obj is MemberMeta t && t == this;
    }

    public override int GetHashCode()
    {
        return (Type.Id, Name).GetHashCode();
    }
}

internal readonly struct MemberContext(
    in MemberMeta target,
    in MemberMeta source,
    bool ignore,
    string? defaultBang,
    string? bang)
{
    private readonly MemberMeta _target = target, _source = source;
    internal readonly bool Ignore = ignore;

    public MemberContext SetupNullability(out bool checkNull, out string? outBang)
    {
        outBang = (checkNull = _target.IsNullable && !_source.IsNullable)
            ? defaultBang
            : bang;
        return this;
    }
}