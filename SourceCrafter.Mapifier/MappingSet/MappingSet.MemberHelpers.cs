﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Mapifier.Helpers;

using System;
using System.Runtime.CompilerServices;
using System.Text;
using SourceCrafter.Mapifier.Constants;

namespace SourceCrafter.Mapifier;

internal sealed partial class MappingSet
{
    private void BuildMemberMapping(
        StringBuilder code,
        bool isFill,
        ref string? comma,
        string spacing,
        ValueBuilder? generateSourceValue,
        MemberMeta source,
        MemberMeta target,
        string copyTargetMethodName,
        string fillTargetMethodName,
        bool sourceRequiresMapper,
        bool sourceHasScalarConversion)
    {
        var checkNull = (!target.IsNullable || !target.Type.IsPrimitive || sourceRequiresMapper || sourceHasScalarConversion) && source.IsNullable;

        if (isFill)
        {
            TypeMeta targetType = target.Type, sourceType = source.Type;

            bool
                isNotAssignable = (target.IsReadOnly || target.IsInit)
                    && (!target.IsProperty || target.IsAutoProperty),
                useUnsafeWriter = target.OwningType?.IsTupleType is not true
                    && isNotAssignable
                    && (targetType.IsValueType || targetType.HasMembers);

            if (useUnsafeWriter && !_canUseUnsafeAccessor) return;

            bool
                useFillMethod = !sourceHasScalarConversion && sourceRequiresMapper && targetType is { IsIterable: false, HasMembers: true, IsPrimitive: false },
                useCopyMethod = !sourceHasScalarConversion && sourceRequiresMapper && (!targetType.IsIterable || targetType.HasMembers),
                isValueType = targetType is not { IsValueType: false, IsStruct: false },
                isParentValueType = target.OwningType is not { IsValueType: false, IsStruct: false },
                useNullUnsafeWriter = isValueType && target.IsNullable;

            var targetMemberName = target.Name;

            if (useUnsafeWriter)
            {
                string
                    getPrivFieldMethodName = $"{targetMemberName}From{target.OwningType!.SanitizedName}",
                    targetOwnerXmlDocType = target.OwningType.NotNullFullName.Replace("<", "{").Replace(">", "}");

                target.OwningType!.UnsafePropertyFieldsGetters.Add(new(targetMemberName, $@"
    /// <summary>
    /// Gets a reference to {(target.IsProperty ? $@"the backing field of <see cref=""{targetOwnerXmlDocType}.{targetMemberName}""/> property" : $"the field <see cref=\"{targetOwnerXmlDocType}.{targetMemberName}\"/>")}
    /// </summary>
    /// <param name=""_""><see cref=""{targetOwnerXmlDocType}""/> container reference of {targetMemberName} {(target.IsProperty ? "property" : "field")}</param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ""{(target.IsProperty ? $"<{targetMemberName}>k__BackingField" : targetMemberName)}"")]
    extern static ref {targetType}{(target.IsNullable ? "?" : null)} {getPrivFieldMethodName}({target.OwningType.NotNullFullName} _);
"));

                if (source.IsNullable)
                {
                    if (target.IsNullable && sourceType.HasMembers)
                    {
                        code.Append(@"
        if (source.").Append(source.Name).Append(@" != null)
            if(target.").Append(targetMemberName).Append(@" != null) 
                ");
                        string targetValue;

                        if (useFillMethod)
                        {
                            code.Append(fillTargetMethodName).Append("(");

                            if (isValueType)
                            {
                                code.Append("ref ");
                            }

                            code.CaptureGeneratedString(() => BuildTargetValue(code), out targetValue).Append("!, ");

                            BuildSourceParam(fill: true);

                            code.Append(")");
                        }
                        else
                        {
                            code.CaptureGeneratedString(() => BuildTargetValue(code), out targetValue).Append(" = ");

                            BuildSourceParam();
                        }

                        code.Append(@";
            else
                ").Append(targetValue).Append(" = "); 
                        
                        BuildSourceParam(); 
                        
                        code.Append(@";
        else
            ").Append(targetValue).Append(" = default").Append(source.DefaultBang).Append(';');
                    }
                    else
                    {
                        code.Append(@"
        if (source.").Append(source.Name).Append(@" is not null)
            ");
                        string targetValue;

                        if (useFillMethod)
                        {
                            code.Append(fillTargetMethodName).Append("(");

                            if (isValueType) code.Append("ref ");

                            code.CaptureGeneratedString(() => BuildTargetValue(code), out targetValue).Append(", ");

                            BuildSourceParam(fill: true);
                                
                            code.Append(")");
                        }
                        else
                        {
                            code.CaptureGeneratedString(() => BuildTargetValue(code), out targetValue).Append(" = ");
                            
                            BuildSourceParam();
                        }

                        code.Append(@";
        else
            ").Append(targetValue).Append(" = default").Append(source.DefaultBang).Append(';');
                    }
                }
                else
                {
                    code.Append(@"
        ");

                    if (useFillMethod)
                    {
                        code.Append(fillTargetMethodName).Append("(");

                        if (isValueType)
                        {
                            code.Append("ref ");
                        }

                        code.CaptureGeneratedString(() => BuildTargetValue(code), out _).Append(", ");

                        BuildSourceParam(fill: true);

                        code.Append(")");
                    }
                    else
                    {
                        code.CaptureGeneratedString(() => BuildTargetValue(code), out _).Append(" = ");

                        BuildValue();
                    }

                    code.Append(@";");
                }

                void BuildTargetValue(StringBuilder code, string targetPrefix = "target.")
                {
                    if (isNotAssignable)
                    {
                        targetPrefix = getPrivFieldMethodName + "(";

                        if (isParentValueType)
                        {
                            targetPrefix += "ref ";
                        }

                        code.Append(targetPrefix).Append("target)");
                    }
                    else
                    {
                        code.Append(targetPrefix + targetMemberName);
                    }
                }
            }
            else if (target is { IsProperty: true, IsInit: false, IsReadOnly: false } or { OwningType.IsTupleType: true })
            {
                code.Append(@"
        target.").Append(targetMemberName).Append(" = ");

                BuildValue();

                code.Append(';');
            }

            void BuildSourceParam(string sourceExpr = "source.", bool fill = false)
            {
                var pos = code.Length;

                code.Append(sourceExpr).Append(source.Name);

                code.Append(source is { IsNullable: true, Type.IsValueType: true } ? ".Value" : source.Bang);

                if (!fill && useCopyMethod)
                {
                    code.Insert(pos, '(').Insert(pos, copyTargetMethodName).Append(')');
                }
            }
        }
        else if (target is { CanBeInitialized: true, IsAccessible: true })
        {
            code.Append(Exchange(ref comma, ",")).Append(spacing);

            if (target.OwningType?.IsTupleType is not true)
            {
                code.Append(target.Name).Append(" = ");
            }

            BuildValue();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void BuildValue()
        {
            GenerateValue(
                code,
                "source." + source.Name,
                generateSourceValue,
                checkNull,
                (!sourceHasScalarConversion || !source.Type.IsValueType) && sourceRequiresMapper,
                target.Type.IsValueType,
                source.Bang,
                source.DefaultBang);
        }
    }

    private bool DiscardMapping(bool ignoreCase, MemberMeta source, MemberMeta target, out bool ignoreSource, out bool ignoreTarget)
    {
        ignoreSource = ignoreTarget = false;

        var casing = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return !(AreSimilarNames(source, target, casing)
            | CheckMappability(casing, target, source, ref ignoreSource, ref ignoreTarget)
            | CheckMappability(casing, source, target, ref ignoreTarget, ref ignoreSource));
    }

    private bool CheckMappability(StringComparison casing, MemberMeta target, MemberMeta source, ref bool ignoreTarget, ref bool ignoreSource)
    {
        if (target.IsReadOnly || source.IsWriteOnly)
            return false;

        var canWrite = false;

        if (target.Attributes.IsDefaultOrEmpty) return canWrite;

        foreach (var attr in target.Attributes)
        {
            if (attr.AttributeClass?.ToGlobalNamespaced() is not { } className) continue;

            if (className == "global::SourceCrafter.Mapifier.Attributes.IgnoreBindAttribute")
            {
                switch ((IgnoreBind)(int)attr.ConstructorArguments[0].Value!)
                {
                    case IgnoreBind.Target:
                        ignoreTarget = true;
                        return false;
                    case IgnoreBind.Both:
                        ignoreTarget = ignoreSource = true;
                        return false;
                    case IgnoreBind.Source:
                        ignoreSource = true;
                        break;
                }

                if (ignoreTarget && ignoreSource)
                    return false;

                continue;
            }

            if (className == "global::SourceCrafter.Mapifier.Attributes.MaxAttribute")
            {
                target.MaxDepth = (short)attr.ConstructorArguments[0].Value!;
                continue;
            }

            if (className != "global::SourceCrafter.Mapifier.Attributes.BindAttribute")
                continue;

            if ((attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0].Expression
                 is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                }
                 && (SymbolEqualityComparer.Default.GetHashCode(compilation.GetSemanticModel(id.SyntaxTree).GetSymbolInfo(id).Symbol) == source.Id
                     || AreSimilarNames(source.Type.Name, (string)(attr.ConstructorArguments[0].Value ?? ""), target.Type.Name, target.Name, casing)))
            {
                canWrite = true;

                switch ((IgnoreBind)(int)attr.ConstructorArguments[1].Value!)
                {
                    case IgnoreBind.Target:
                        ignoreTarget = true;
                        return false;
                    case IgnoreBind.Both:
                        ignoreTarget = ignoreSource = true;
                        return false;
                    case IgnoreBind.Source:
                        ignoreSource = true;
                        break;
                }

                if (ignoreTarget && ignoreSource)
                    return false;
            }
        }
        return canWrite;
    }

    private bool AreSimilarNames(string sourceTypeName, string sourceMemberName, string targetTypeName, string targetMemberName, StringComparison casing)
    {
        return targetMemberName.Equals(sourceMemberName, casing)
                || targetMemberName.Equals(targetTypeName + sourceMemberName, casing)
                || (sourceTypeName + targetMemberName).Equals(sourceMemberName, casing);
    }

    private static bool AreSimilarNames(MemberMeta source, MemberMeta target, StringComparison casing)
    {
        return target.Name.Equals(source.Name, casing)
                || target.Name.Equals(target.Type.Name + source.Name, casing)
                || (source.Type.Name + target.Name).Equals(source.Name, casing);
    }

    private delegate void MemberBuilder(StringBuilder code, bool isFill);
}
