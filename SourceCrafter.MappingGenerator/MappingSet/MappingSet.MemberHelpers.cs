using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;
using System;
using System.Linq;
using System.Text;

namespace SourceCrafter.Bindings;

delegate string BuildMember(
    ValueBuilder createType,
    TypeData targetType,
    Member targetMember,
    TypeData sourceType,
    Member sourceMember);

internal sealed partial class MappingSet
{
    string? BuildTypeMember(
        bool isFill,
        ref string? comma,
        string spacing,
        ValueBuilder createType,
        Member source,
        Member target,
        string sourceToTargetMethodName,
        string fillTargetFromMethodName,
        bool call)
    {
        bool checkNull = (!target.IsNullable || !target.Type.IsPrimitive) && source.IsNullable;

        var _ref = target.Type.IsValueType ? "ref " : null;

        if (isFill)
        {
            string
                sourceMember = "source." + source.Name,
                targetMember = "target." + target.Name,
                targetType = target.Type.NotNullFullName,
                parentFullName = target.OwningType!.NotNullFullName,
                targetDefaultBang = target.DefaultBang!,
                getPrivFieldMethodName = $"Get{target.OwningType!.SanitizedName}_{target.Name}_PF",
                getNullMethodName = $"GetValueOfNullableOf{target.Type.SanitizedName}";

            var declareRef = !target.IsWritable || target.IsInit || target.Type.IsValueType;

            if (target.Type.IsMultiMember && declareRef)
            {
                if (!canUseUnsafeAccessor)
                    return null;

                if (target.IsNullable)
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
            }

            if (!target.Type.IsMultiMember && (!target.IsWritable || target.OwningType?.IsReadOnly is true || (target.OwningType is { IsTupleType: false } && target.IsInit)))
                return null;

            string
                outputVal = "_target" + target.Name,
                inputVal = "_source" + target.Name,
                fillFirstParam = declareRef
                    ? target.IsNullable
                        ? $"{_ref}{getNullMethodName}(ref {outputVal})"
                        : $"{_ref}{outputVal}"
                    : targetMember;

            var start = declareRef
                ? $@"
        // Nullable hack to fill without changing reference
        ref var {outputVal} = ref {getPrivFieldMethodName}(target);"
                : null;

            if (target.Type.IsMultiMember) // Usar el metodo Fill en vez de asignar
            {
                if (target.IsNullable)
                {
                    var notNullRightSideCheck = target.Type.IsStruct ? ".HasValue" : " is not null";
                    if (source.IsNullable)
                    {
                        return $@"{start}
        if ({sourceMember} is {{}} {inputVal})
            if({targetMember}{notNullRightSideCheck})
                {fillTargetFromMethodName}({fillFirstParam}, {inputVal});{(declareRef ? $@"
            else
                {outputVal} = {sourceToTargetMethodName}({inputVal});
        else
            {outputVal} = default{targetDefaultBang};" : $@"
            else
                {targetMember} = {sourceToTargetMethodName}({inputVal});
        else
            {targetMember} = default{targetDefaultBang};")}
";
                    }

                    return $@"{start}
        if({outputVal}{notNullRightSideCheck})
            {fillTargetFromMethodName}({fillFirstParam}, {sourceMember});{(declareRef ? $@"
        else
            {outputVal} = {sourceToTargetMethodName}({sourceMember});" : $@"
        else
            {targetMember} = {sourceToTargetMethodName}({sourceMember});")}";

                }
                else if (source.IsNullable)
                {

                    return $@"{start}
        if({sourceMember} is {{}} _in)
            {fillTargetFromMethodName}({fillFirstParam}, _in);{(declareRef ? $@"
        else
            {outputVal} = default{targetDefaultBang};" : $@"
        else
            {targetMember} = default{targetDefaultBang};")}
";
                }

                return $@"{start}
        {fillTargetFromMethodName}({fillFirstParam}, {sourceMember});
";
            }

            return $@"
        {targetMember} = {GenerateValue(sourceMember, createType, checkNull, call, target.Type.IsValueType, source.Bang, source.DefaultBang)};";

        }
        else
        {
            return Exch(ref comma, ",") + spacing + (target.OwningType?.IsTupleType is not true ? target.Name + " = " : null)
                + GenerateValue("source." + source.Name, createType, checkNull, call, target.Type.IsValueType, source.Bang, source.DefaultBang);
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
        if (!target.IsWritable || !source.IsReadable)
            return false;

        bool canWrite = false;

        foreach (var attr in target.Attributes)
        {
            if (attr.AttributeClass?.ToGlobalNamespace() is not { } className) continue;

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

    static bool IsNotMappable(ISymbol member, out ITypeSymbol typeOut, out int typeIdOut, out Member memberOut)
    {
        var isAccesible = member.DeclaredAccessibility is Accessibility.Internal or Accessibility.Public or Accessibility.ProtectedAndInternal or Accessibility.ProtectedOrInternal;

        switch (member)
        {
            case IPropertySymbol
            {
                ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                IsIndexer: false,
                Type: { } type,
                IsReadOnly: var isReadonly,
                IsWriteOnly: var isWriteOnly,
                SetMethod.IsInitOnly: var isInitOnly
            }:
                (typeOut, typeIdOut, memberOut) = (
                    type.AsNonNullable(),
                    GetId(type),
                    new(_comparer.GetHashCode(member),
                        member.ToNameOnly(),
                        type.IsNullable(),
                        isAccesible && !isWriteOnly,
                        isAccesible && !isReadonly,
                        member.GetAttributes(),
                        isInitOnly == true,
                        isAccesible,
                        true));

                return false;

            case IFieldSymbol
            {
                ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                Type: { } type,
                AssociatedSymbol: null,
                IsReadOnly: var isReadonly
            }:
                (typeOut, typeIdOut, memberOut) =
                    (type.AsNonNullable(),
                     GetId(type),
                     new(_comparer.GetHashCode(member),
                        member.ToNameOnly(),
                        type.IsNullable(),
                        isAccesible,
                        isAccesible && !isReadonly,
                        member.GetAttributes(),
                        isAccesible
                    ));

                return false;

            default:
                (typeOut, typeIdOut, memberOut) = (default!, default, default!);

                return true;
        }
    }
}
