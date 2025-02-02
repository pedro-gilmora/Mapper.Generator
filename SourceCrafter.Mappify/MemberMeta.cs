﻿using System;
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

    internal readonly string Name = name, UnsafeFieldAccesor = privateFieldMethodName;
    internal readonly TypeMeta Type = type;
    // internal readonly bool IsNullable;

    internal readonly TypeMeta? OwningType = owningType;

    private readonly HashSet<int> _ignores = ignoreFor ?? [], _matches = manualMatches ?? [];

    internal readonly bool 
        CanRead = canRead, 
        CanWrite = canWrite,
        IsNullable = isNullable, 
        UseUnsafeAccessor = useUnsafeAccessor;

    internal readonly short MaxDepth = maxDepth;

    internal bool IsMatchingContext(in MemberMeta source, bool ignoreCase, out bool isTargetAssignable, out bool isSourceAssignable)
    {
        if (_id != source._id
            && !Name.Equals(source.Name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && !source.Name.Equals(Type.Name + Name)
            && !Name.Equals(source.Type.Name + source.Name)
            && !source._matches.Contains(_id)
            && !_matches.Contains(source._id))
        {
            return isTargetAssignable = isSourceAssignable = false;
        }

        bool areMatechedByAttribute = source._matches.Contains(_id) || _matches.Contains(source._id);
        bool targetIgnoresSource = _ignores.Contains(source._id);
        bool sourceIgnoresTarget = source._ignores.Contains(_id);
        bool targetCanBeAssigned = source.CanRead/* && OwningType?.IsInterface is not true*/ && (CanWrite || UseUnsafeAccessor);
        bool sourceCanBeAssigned = CanRead/* && source.OwningType?.IsInterface is not true*/ && (source.CanWrite || source.UseUnsafeAccessor);
        

        return (isTargetAssignable = (areMatechedByAttribute || !targetIgnoresSource) && targetCanBeAssigned)
            | (isSourceAssignable = (areMatechedByAttribute || !sourceIgnoresTarget) && sourceCanBeAssigned);
    }



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
    internal readonly bool IsValid = ignore;

    public MemberContext SetupNullability(out bool checkNull, out string? outBang)
    {
        outBang = (checkNull = _target.IsNullable && !_source.IsNullable)
            ? defaultBang
            : bang;
        return this;
    }
}