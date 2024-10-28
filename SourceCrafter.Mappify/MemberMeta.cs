using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Helpers;

namespace SourceCrafter.Mappify;

internal class MemberMeta
{
    public readonly int Id;

    internal readonly string Name;
    internal readonly TypeMeta Type;
    internal readonly bool IsInitOnly;
    internal readonly bool IsAccessible;
    internal readonly bool IsReadOnly;
    internal readonly bool IsWriteOnly;
    internal readonly bool IsAutoProperty;
    internal readonly int Position;
    internal readonly ImmutableArray<AttributeData> Attributes = ImmutableArray<AttributeData>.Empty;
    internal readonly bool CanBeInitialized;
    internal short MaxDepth;
    internal readonly bool IsNullable;
    internal readonly TypeMeta OwningType;

    public MemberMeta(int id,
        IPropertySymbol member,
        TypeMeta type,
        TypeMeta owningType,
        bool isAccessible,
        bool isAutoProperty,
        int position)
    {
        Id = id;
        Name = member.ToNameOnly();
        Type = type;
        OwningType = owningType;
        IsInitOnly = member.SetMethod?.IsInitOnly is true;
        IsAccessible = isAccessible;
        IsReadOnly = member.IsReadOnly;
        IsWriteOnly = member.IsWriteOnly;
        IsAutoProperty = isAutoProperty;
        Position = position;
        Attributes = member.GetAttributes();
        IsNullable = member.IsNullable();
    }

    public MemberMeta(int id,
        IFieldSymbol member,
        TypeMeta type,
        TypeMeta owningType,
        bool isAccessible,
        int position)
    {
        Id = id;
        Name = member.ToNameOnly();
        Type = type;
        OwningType = owningType;
        IsInitOnly = !member.IsReadOnly;
        IsAccessible = isAccessible;
        Position = position;
        IsReadOnly = member.IsReadOnly;
        CanBeInitialized = !IsReadOnly;
    }

    public MemberMeta(
        int id,
        string name,
        bool isReadOnly,
        bool isNullable,
        TypeMeta type,
        TypeMeta owningType,
        bool isAccessible,
        int position)
    {
        Id = id;
        Name = name;
        Type = type;
        OwningType = owningType;
        IsInitOnly = !isReadOnly;
        IsAccessible = isAccessible;
        Position = position;
        IsReadOnly = isReadOnly;
        IsNullable = isNullable;
        CanBeInitialized = !IsReadOnly;
    }

    internal bool CantMap(bool ignoreCase, MemberMeta target, out MemberContext sourceCtx, out MemberContext targetCtx)
    {
        sourceCtx = targetCtx = default;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if ((Name.Equals(target.Name, comparison)
             || Name.Equals(Type.Name + target.Name, comparison)
             || (target.Type.Name + Name).Equals(target.Name, comparison)
             || (target.Type.Name + Name).Equals(Type.Name + target.Name, comparison))
            | CantMap(this, target, ref targetCtx, ref sourceCtx)
            | CantMap(target, this, ref sourceCtx, ref targetCtx))

            return false;

        sourceCtx.DefaultBang = GetDefaultBangChar(target.IsNullable, IsNullable, Type.Symbol.AllowsNull());
        sourceCtx.Bang = GetBangChar(target.IsNullable, IsNullable);
        targetCtx.DefaultBang = GetDefaultBangChar(IsNullable, target.IsNullable, target.Type.Symbol.AllowsNull());
        targetCtx.Bang = GetBangChar(IsNullable, target.IsNullable);

        return true;
    }

    private static string? GetDefaultBangChar(bool isTargetNullable, bool isSourceNullable, bool sourceAllowsNull)
        => !isTargetNullable && (sourceAllowsNull || isSourceNullable) ? "!" : null;

    private static string? GetBangChar(bool isTargetNullable, bool isSourceNullable)
        => !isTargetNullable && isSourceNullable ? "!" : null;

    private bool CantMap(MemberMeta target, MemberMeta source, ref MemberContext sourceMeta, ref MemberContext targetMeta)
    {
        if (target.IsReadOnly || source.IsWriteOnly || target.Attributes.IsDefaultOrEmpty) return false;

        var canWrite = false;

        foreach (var attr in target.Attributes)
        {
            if (attr.AttributeClass?.ToGlobalNamespaced() is not { } className) continue;

            switch (className)
            {
                case "global::SourceCrafter.Bindings.Attributes.IgnoreBindAttribute":

                    switch ((Applyment)(int)attr.ConstructorArguments[0].Value!)
                    {
                        case Applyment.Target:
                            targetMeta.Ignore = true;
                            return false;
                        case Applyment.Both:
                            targetMeta.Ignore = sourceMeta.Ignore = true;
                            return false;
                        case Applyment.Source:
                            sourceMeta.Ignore = true;
                            break;
                        case Applyment.None:
                        default: break;
                    }

                    if (targetMeta.Ignore && sourceMeta.Ignore) return false;

                    continue;

                case "global::SourceCrafter.Bindings.Attributes.MaxAttribute":

                    targetMeta.MaxDepth = (short)attr.ConstructorArguments[0].Value!;

                    continue;
            }

            if (className != "global::SourceCrafter.Bindings.Attributes.BindAttribute")
                continue;

            if ((attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0].Expression is not InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                }
                || SymbolEqualityComparer.Default
                    .GetHashCode(Type.Types.Compilation.GetSemanticModel(id.SyntaxTree)
                    .GetSymbolInfo(id).Symbol) != source.Id)

                continue;

            canWrite = true;

            switch ((Applyment)(int)attr.ConstructorArguments[1].Value!)
            {
                case Applyment.Target:
                    targetMeta.Ignore = true;
                    return false;
                case Applyment.Both:
                    targetMeta.Ignore = sourceMeta.Ignore = true;
                    return false;
                case Applyment.Source:
                    sourceMeta.Ignore = true;
                    break;
                case Applyment.None:
                default: break;
            }

            if (targetMeta.Ignore && sourceMeta.Ignore) return false;
        }

        return canWrite;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MemberMeta a, MemberMeta b)
    {
        return (a.Type.Id, a.Name) == (b.Type.Id, b.Name);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MemberMeta a, MemberMeta b)
    {
        return (a.Type.Id, a.Name) != (b.Type.Id, b.Name);
    }
}

internal record struct MemberContext
{
    internal bool Ignore;
    internal string? DefaultBang, Bang;
    internal short MaxDepth;
}