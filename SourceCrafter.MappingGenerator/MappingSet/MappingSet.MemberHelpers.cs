using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;

using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace SourceCrafter.Bindings;

delegate string BuildMember(
    ValueBuilder createType,
    TypeData targetType,
    Member targetMember,
    TypeData sourceType,
    Member sourceMember);

[Flags]
file enum GenerateUse
{
    OriginalTarget,
    NullableDesignatedTarget,
    UnsafeUnderlyingTarget,
    UnsafeUnderlyingTargetValue,
    EqualsAssignment,
    FillMethod,
    CopyMethod,
    RefFieldAsParamToUpdate,

}

internal sealed partial class MappingSet
{
    string? BuildTypeMember(
        bool isFill,
        ref string? comma,
        string spacing,
        ValueBuilder createType,
        Member source,
        Member target,
        string copyMethodName,
        string fillMethodName,
        bool callable)
    {
        bool checkNull = (!target.IsNullable || !target.Type.IsPrimitive) && source.IsNullable;

        if (isFill)
        {
            bool
                notAssignable = (target.IsReadOnly || target.IsInit) && (!target.IsProperty || target.IsAutoProperty),
                useRefFromUnsafeAccessor = notAssignable && (target.Type.IsValueType || target.Type.HasMembers) && target.OwningType?.IsTupleType is not true;

            string
                _ref = target.Type.IsValueType ? "ref " : "",
                sourceMember = "source." + source.Name,
                targetMember = "target." + target.Name,
                targetType = target.Type.NotNullFullName,
                parentFullName = target.OwningType!.NotNullFullName,
                targetDefaultBang = target.DefaultBang!,
                getPrivFieldMethodName = $"Get{target.OwningType!.SanitizedName}_{target.Name}_PF",
                getNullMethodName = $"GetValueOfNullableOf{target.Type.SanitizedName}";

            if (useRefFromUnsafeAccessor)
            {
                if (!canUseUnsafeAccessor) return null;

                if (target.IsNullable && target.Type.IsValueType)
                {
                    target.Type.NullableMethodUnsafeAccessor ??= new($@"
    /// <summary>
    /// Gets a reference to the not nullable value of <see cref=""global::System.Nullable{{{targetType}}}""/>
    /// </summary>
    /// <param name=""_"">Target nullable type</param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ""value"")]
    extern static ref {targetType} {getNullMethodName}(ref {targetType}? _);
");
                }

                target.OwningType!.UnsafePropertyFieldsGetters.Add(new(targetMember, $@"
    /// <summary>
    /// Gets a reference to {(target.IsProperty ? $"the backing field of {parentFullName}.{target.Name} property" : $"the field {parentFullName}.{target.Name}")}
    /// </summary>
    /// <param name=""_""><see cref=""global::System.Nullable{{{targetType}}}""/> container reference of {target.Name} {(target.IsProperty ? "property" : "field")}</param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ""{(target.IsProperty ? $"<{target.Name}>k__BackingField" : target.Name)}"")]
    extern static ref {targetType}{(target.IsNullable ? "?" : null)} {getPrivFieldMethodName}({parentFullName} _);
"));

                string
                    outputVal = "_target" + target.Name,
                    inputVal = "_source" + target.Name,
                    fillFirstParam = useRefFromUnsafeAccessor
                        ? target.IsNullable && target.Type.IsValueType
                            ? $"{_ref}{getNullMethodName}(ref {outputVal})"
                            : $"{_ref}{outputVal}"
                        : targetMember;

                var start = $@"
        // Nullable hack to fill without changing reference
        ref var {outputVal} = ref {getPrivFieldMethodName}(target);";

                if (target.IsNullable)
                {
                    var notNullTargetCheck = NotNullCheck(target);

                    if (source.IsNullable)
                    {
                        var notNullSourceCheck = NotNullCheck(source);

                        return $@"{start}
        if ({sourceMember} is {{}} {inputVal})
            if({targetMember}{notNullTargetCheck})
                {fillMethodName}({fillFirstParam}, {inputVal});{(useRefFromUnsafeAccessor ? $@"
            else
                {outputVal} = {copyMethodName}({inputVal});
        else
            {outputVal} = default{targetDefaultBang};" : $@"
            else
                {targetMember} = {copyMethodName}({GetValue(inputVal)});
        else
            {targetMember} = default{targetDefaultBang};")}
";
                    }

                    return $@"{start}
        if({outputVal}{notNullTargetCheck})
            {fillMethodName}({fillFirstParam}, {sourceMember});{(useRefFromUnsafeAccessor ? $@"
        else
            {outputVal} = {copyMethodName}({GetValue(sourceMember)});" : $@"
        else
            {targetMember} = {copyMethodName}({GetValue(sourceMember)});")}";

                }
                else if (source.IsNullable)
                {
                    return $@"{start}
        if({sourceMember} is {{}} _in)
            {fillMethodName}({fillFirstParam}, {GetValue("_in")});{(useRefFromUnsafeAccessor ? $@"
        else
            {outputVal} = default{targetDefaultBang};" : $@"
        else
            {targetMember} = default{targetDefaultBang};")}
";
                }

                string value = GetValue(sourceMember);

                return start + @"
        " + (target.Type.IsIterable is true || !target.Type.HasMembers
                    ? $"{outputVal} = {value}"
                    : fillMethodName + $"({fillFirstParam}, {sourceMember})") + ";";


            }

            string value2 = GetValue(sourceMember);

            return $@"
        {targetMember} = {value2};";

        }
        else if (target.CanInit && target.IsWritableAsTarget)
        {
            return Exch(ref comma, ",") + spacing + (target.OwningType?.IsTupleType is not true ? target.Name + " = " : null)
                + GetValue("source." + source.Name);
        }

        return null;

        //Add agressive inlining spec
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        string GetValue(string expr)
        {
            return GenerateValue(expr, createType, checkNull, callable, target.Type.IsValueType, source.Bang, source.DefaultBang);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string NotNullCheck(Member target)
        {
            return target.Type.IsStruct ? ".HasValue" : " is not null";
        }
    }

    bool AreNotMappableByDesign(bool ignoreCase, Member source, Member target, out bool ignoreSource, out bool ignoreTarget)
    {
        ignoreSource = ignoreTarget = false;

        return !(target.Name.Equals(
            source.Name,
            ignoreCase
                ? StringComparison.InvariantCultureIgnoreCase
                : StringComparison.InvariantCulture)
            | CheckMappability(target, source, ref ignoreSource, ref ignoreTarget)
            | CheckMappability(source, target, ref ignoreTarget, ref ignoreSource));
    }

    bool CheckMappability(Member target, Member source, ref bool ignoreTarget, ref bool ignoreSource)
    {
        if (target.IsReadOnly || source.IsWriteOnly)
            return false;

        bool canWrite = false;

        if (target.Attributes.IsDefaultOrEmpty) return canWrite;

        foreach (var attr in target.Attributes)
        {
            if (attr.AttributeClass?.ToGlobalNamespaced() is not { } className) continue;

            if (className == "global::SourceCrafter.Bindings.Attributes.IgnoreBindAttribute")
            {
                switch ((ApplyOn)(int)attr.ConstructorArguments[0].Value!)
                {
                    case ApplyOn.Target:
                        ignoreTarget = true;
                        return false;
                    case ApplyOn.Both:
                        ignoreTarget = ignoreSource = true;
                        return false;
                    case ApplyOn.Source:
                        ignoreSource = true;
                        break;
                }

                if (ignoreTarget && ignoreSource)
                    return false;

                continue;
            }

            if (className == "global::SourceCrafter.Bindings.Attributes.MaxAttribute")
            {
                target.MaxDepth = (short)attr.ConstructorArguments[0].Value!;
                continue;
            }

            if (className != "global::SourceCrafter.Bindings.Attributes.BindAttribute")
                continue;

            if ((attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0].Expression
                 is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                }
                 && _comparer.GetHashCode(compilation.GetSemanticModel(id.SyntaxTree).GetSymbolInfo(id).Symbol) == source.Id)
            {
                canWrite = true;

                switch ((ApplyOn)(int)attr.ConstructorArguments[1].Value!)
                {
                    case ApplyOn.Target:
                        ignoreTarget = true;
                        return false;
                    case ApplyOn.Both:
                        ignoreTarget = ignoreSource = true;
                        return false;
                    case ApplyOn.Source:
                        ignoreSource = true;
                        break;
                }

                if (ignoreTarget && ignoreSource)
                    return false;
            }
        }
        return canWrite;
    }

    delegate void MemberBuilder(StringBuilder code, bool isFill);
}
