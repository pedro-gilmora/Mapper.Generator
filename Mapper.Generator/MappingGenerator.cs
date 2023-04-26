#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Text;
using static System.Net.Mime.MediaTypeNames;
using Mapper.Generator.Constants;
using System.Diagnostics;

namespace Mvvm.Extensions.Generator;

[Generator]
public class MappingGenerator : IIncrementalGenerator
{
    public const string DiagnosticId = "NotAllowedIgnoreAndMap";

    private static readonly LocalizableString Title = "Two attributes on the same property";
    private static readonly LocalizableString MessageFormat = "Property '{0}' has both, Ignore and Map attributes";
    private static readonly LocalizableString Description = "Ignore and Map attributes attributes should not be present on the same property.";
    private const string Category = "Naming";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

    static readonly object _lock = new();
    static readonly Func<ISymbol?, ISymbol?, bool> AreSymbolsEquals = SymbolEqualityComparer.Default.Equals;
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        lock (_lock)
        {
            var interfaceDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "Mapper.Generator.Attributes.MapAttribute`1",
                    static (n, _) => n is ClassDeclarationSyntax or RecordDeclarationSyntax,
                    static (ctx, c) => new { attrs = ctx.Attributes, type = (ITypeSymbol)ctx.TargetSymbol, model = ctx.SemanticModel }
                );

            context.RegisterSourceOutput(
                interfaceDeclarations,
                static (sourceProducer, interfaceToGenerate) =>
                {
                    var (name, code) = CreateRelatedTypeFiles(interfaceToGenerate.type, interfaceToGenerate.model, interfaceToGenerate.attrs);
                    sourceProducer.AddSource(name, code);
                }
            );
        }
    }

    public static (string, string) CreateRelatedTypeFiles(ITypeSymbol cls, SemanticModel model, ImmutableArray<AttributeData> attrs)
    {
        HashSet<string>
            usings = new() { };
        string
            name = cls.Name,
            nmspc = cls.ContainingNamespace.ToDisplayString();

        try
        {
            //GenerateMembers(model, usings, GetAllMembers(target), out string fields, out string properties, out string propertyChangeFields, out string commandMethods);

#if DEBUG
            string extraText = "";
#endif
            var code = new StringBuilder($@"namespace {nmspc};

public partial class {name}
{{
    ");
            GetImplicitOperators(code, attrs, cls, model, usings
#if DEBUG
                , ref extraText
#endif
            );

#if DEBUG
            code.Append($@"
}}
/*
Extras:
-------{extraText}
*/
");

#else
            code.Append(@"
}");
#endif
            return ($"{nmspc}.{name}.mapper.g.cs", $@"//<auto generated>
#nullable enable{usings.Join(u => $@"
using {u};")}

{code}");

        }
        catch (Exception e)
        {
            return ($"{nmspc}.{name}.mapper.g.cs", $"/*{e}*/");
        }

    }

    static void GetImplicitOperators(StringBuilder code, ImmutableArray<AttributeData> attrs, ITypeSymbol leftClass, SemanticModel model, HashSet<string> usings
#if DEBUG
        , ref string extraText
#endif
        )
    {
        List<string> builders = new();
        foreach (var attr in attrs.AsSpan())
        {
            var rightClass = attr.AttributeClass!.TypeArguments.First();
            string rightClassName = GetType(usings, rightClass);
            string leftClassName = GetType(usings, leftClass);

            Dictionary<ISymbol, ISymbol> reverseMappers = new(SymbolEqualityComparer.Default);

            code
                .AppendFormat(@"public static explicit operator {0}({1} from)
    {{
        return ",
                    leftClassName,
                    rightClassName);

            if (UsesStaticMapper(attr, leftClass, rightClass, model, out var staticMapper))
            {
                GetInitializers(leftClass, rightClass, model, usings, reverseMappers
#if DEBUG
        , ref extraText
#endif
                );

                code.Append($"{staticMapper}(from);");

            }
            else
            {
                code.Append($"new {leftClass.Name} {{");

                GetInitializers(leftClass, rightClass, model, usings, reverseMappers
#if DEBUG
                    , ref extraText
#endif
                    , code.Append);

                code.Append(@"
        }");

                builders.Aggregate(code, (_, i) => code.Append(i)).Append(";");
            }

            code.Append(@"
    }");

            builders.Clear();

            code
                .AppendFormat(@"
			
    public static explicit operator {0}({1} from)
    {{
        return ",
                    rightClassName,
                    leftClassName);

            if (UsesStaticMapper(attr, rightClass, leftClass, model, out staticMapper))

                code.Append($"{staticMapper}(from);");

            else
            {
                code.Append($"new {rightClassName} {{");
                bool inserted = false;

                foreach (var map in reverseMappers)
                {
                    GeneratePropertyMap(usings, map.Key, map.Value, code.Append, ref inserted);
                }

                //GetInitializers(code, sourceClass, targetClass, model, builders, usings, true);

                code.Append(@"
        }");

                builders.Aggregate(code, (_, i) => code.Append(i)).Append(";");
            }

            code.Append(@"
    }");
        }
    }

    static void GetInitializers(ITypeSymbol leftClass, ITypeSymbol rightClass, SemanticModel model,
        HashSet<string> usings, Dictionary<ISymbol, ISymbol> reverseMappers
#if DEBUG
        , ref string extraText
#endif
        , Func<string, StringBuilder>? append = null
    )
    {
        var inserted = false;

        var rightMembers = GetMappableMembers(rightClass);

        foreach (var leftMember in GetMappableMembers(leftClass))
        {
            if ((TryMapFromAttribute(leftMember, rightClass, model, out Ignore ignore, out var rightMember
#if DEBUG
                , ref extraText
#endif
                ) ||
                TryMapFromRight(rightMembers, leftMember, out rightMember)) &&
                ignore != Ignore.Both
                )
            {
                if (ignore != Ignore.OnTarget && append != null)
                    GeneratePropertyMap(usings, leftMember, rightMember, append, ref inserted);

                if (ignore != Ignore.OnSource)
                    reverseMappers.Add(rightMember, leftMember);
            }
        }
    }

    static ReadOnlySpan<ISymbol> GetMappableMembers(ITypeSymbol target) =>
        target.GetMembers()
            .Where(member => member is IPropertySymbol or IFieldSymbol { AssociatedSymbol: null })
            .Distinct(SymbolEqualityComparer.Default)
            .ToImmutableArray()
            .AsSpan();

    static bool UsesStaticMapper(
        AttributeData attr, ITypeSymbol leftClass, ITypeSymbol rightClass, SemanticModel model, out string staticMapper)
    {
        staticMapper = null!;

        if (attr.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax
            {
                ArgumentList.Arguments: [{ Expression: { } arg }]
            }) return false;

        if (arg is not InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax
                {
                    //Is nameof expression
                    Identifier.Text: "nameof"
                },
                ArgumentList.Arguments: [
                    {
                        // forces to match a member access sytntax
                        Expression: MemberAccessExpressionSyntax { Name: { } id } expr
                    }]
            }) return false;

        if (model.GetSymbolInfo(id) is not
            {
                CandidateReason: CandidateReason.MemberGroup,
                CandidateSymbols: { } cands
            }) return false;

        foreach (var c in cands)
        {
            if ((c is not IMethodSymbol
                {
                    ReturnType: { } retType,
                    Parameters: [{ Type: { } firstArgType }]
                } ||
                !AreSymbolsEquals(leftClass, retType) ||
                !AreSymbolsEquals(rightClass, firstArgType))
            ) continue;

            staticMapper = expr.ToString();
            return true;
        }
        return false;
    }

    static bool TryMapFromAttribute(
        ISymbol prop, ITypeSymbol rightClass, SemanticModel model,
        out Ignore ignore,
        out ISymbol element
#if DEBUG
        , ref string extraText
#endif
    )
    {
        element = null!;
        ignore = 0;

        foreach (var (attrName, firstArg, ignore2) in prop.GetAttributes().Select(GetAttrInfo).ToImmutableArray().AsSpan())
        {
            if (attrName != "Map")
            {
                if (ignore2 > ignore)
                    ignore = ignore2;
#if DEBUG
                extraText += $@"
Attr: {attrName}: ignoreValue: {ignore}";
#endif
                continue;
            }

            //NotifyUsage Mapping

            else if (firstArg is not InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax
                {
                    //Is nameof expression
                    Identifier.Text: "nameof"
                },
                ArgumentList.Arguments: [
                {
                    // forces to match a member access sytntax
                    Expression: MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax cls,
                        Name: { } id
                    } expr
                }]
            }
            )
                continue;

            else if (
                model.GetSymbolInfo(cls).Symbol is not ITypeSymbol memberType ||
                !AreSymbolsEquals(memberType, rightClass) ||
                (element = model.GetSymbolInfo(id).Symbol!) is not (IPropertySymbol or IFieldSymbol { AssociatedSymbol: null })
            )
                continue;


            if (ignore2 > ignore)
                ignore = ignore2;
#if DEBUG
            extraText += $@"
Attr: {attrName}: ignoreValue: {ignore}";
#endif
            return true;
        }
        return false;
    }



    static (string, ExpressionSyntax?, Ignore) GetAttrInfo(AttributeData attr)
    {
        var args = ((AttributeSyntax)attr.ApplicationSyntaxReference!.GetSyntax()).ArgumentList?.Arguments;
        var name = attr.AttributeClass!.Name!.Replace("Attribute", "");

        return (name, args, attr.ConstructorArguments) switch
        {
            ("Map", [{ Expression: { } expr },..], var ctorArgs) => (
                name, expr, ctorArgs is [_, { Value: int mapValue }] && (Ignore)mapValue is { } val and not Ignore.Both ? val : Ignore.None),

            ("Ignore", var arg, var ctorArgs) => (
                name,
                arg is [{ Expression: { } expr }]
                    ? expr
                    : null,
                ctorArgs is [{ Value: int mapValue }]
                    ? (Ignore)mapValue
                    : Ignore.Both),

            _ => (name, null, Ignore.None)
        };
    }
    static void GeneratePropertyMap(
        HashSet<string> usings, ISymbol leftMember, ISymbol rightMember, Func<string, StringBuilder> append, ref bool inserted)
    {
        if (leftMember is IPropertySymbol { IsReadOnly: true } || rightMember is IPropertySymbol { IsWriteOnly: true })
            return;

        if (inserted)
            append(",");
        else
            inserted = true;

        var assignment = $"from.{rightMember.Name}";
        var mapMemberType = rightMember is IFieldSymbol fldMap
            ? fldMap.Type
            : ((IPropertySymbol)rightMember).Type;
        var memberType = leftMember is IFieldSymbol fld
            ? fld.Type
            : ((IPropertySymbol)leftMember).Type;

        if (!AreSymbolsEquals(memberType, mapMemberType) &&
            (!IsEnumerable(memberType) || !IsEnumerable(mapMemberType) ||
            !ProcessEnumerableConversion(usings, memberType, mapMemberType, memberType.NullableAnnotation == NullableAnnotation.Annotated, ref assignment)))

            assignment = $"({GetType(usings, memberType)}){assignment}";

        append($@"
	        {leftMember.Name} = {assignment}");
    }

    static bool ProcessEnumerableConversion(HashSet<string> usings, ITypeSymbol targetType, ITypeSymbol mapType, bool isNullable, ref string assignment)
    {
        var nullCheck = isNullable ? "?" : "";

        ITypeSymbol
            targetCollectionType = targetType is IArrayTypeSymbol { ElementType: { } eltype } arrSymb
                ? eltype
                : ((INamedTypeSymbol)targetType).TypeArguments[0],
            sourceCollectionType = mapType is IArrayTypeSymbol arrSymb2
                ? arrSymb2.ElementType
                : ((INamedTypeSymbol)mapType).TypeArguments[0];

        var targetCollectionTypeName = GetType(usings, targetCollectionType);

        if (!AreSymbolsEquals(targetCollectionType, sourceCollectionType))
        {
            assignment += $"{nullCheck}.Select(el => ({targetCollectionTypeName})el)";
        }
        if (targetType is IArrayTypeSymbol)
        {
            assignment += $"{nullCheck}.ToArray()";
        }
        else if (targetType.Name.EndsWith("List"))
        {
            assignment += $"{nullCheck}.ToList()";

            if (targetType.Name.StartsWith("IReadOnlyList"))
                assignment = $"({targetCollectionTypeName}){assignment}";
        }
        else if (targetType.Name.EndsWith("HashSet"))
        {
            assignment += $"{nullCheck}.ToHashSet()";
        }
        else
            return false;

        return true;
    }

    static bool IsEnumerable(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;
        foreach (var ai in type.AllInterfaces.AsSpan())
            if (ai.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
        return false;
    }

    static bool TryMapFromRight(ReadOnlySpan<ISymbol> rightMembers, ISymbol leftMember, out ISymbol rightMember)
    {
        rightMember = null!;
        foreach (var member in rightMembers)
        {
            if (member is IPropertySymbol or IFieldSymbol && member.Name == leftMember.Name)
            {
                rightMember = member;
                return true;
            }
        }
        return false;
    }

    static string GetType(HashSet<string> usings, ITypeSymbol type)
    {
        RegisterNamespace(usings, type.ContainingNamespace.ToString()!);

        return type switch
        {
            INamedTypeSymbol { Name: "Nullable", TypeArguments: [{ } underlyingType] }
                => $"{GetType(usings, underlyingType)}?",

            INamedTypeSymbol { IsTupleType: true, TupleElements: var elements }
                => $"({elements.Join(f => $"{GetType(usings, f.Type)}{(f.IsExplicitlyNamedTupleElement ? $" {f.Name}" : "")}", ", ")})",

            INamedTypeSymbol { Name: var name, TypeArguments: { Length: > 0 } generics }
                => $"{name}<{generics.Join(g => GetType(usings, g), ", ")}>",
            _
                => IsPrimitive((INamedTypeSymbol)type) ? type.ToDisplayString() : type.Name
        };
    }

    static void RegisterNamespace(HashSet<string> usings, params string[] namespaces)
    {
        foreach (var ns in namespaces)
            if (ns != "<global namespace>")
                usings.Add(ns);
    }

    static bool IsPrimitive(INamedTypeSymbol type)
    {
        return type?.SpecialType switch
        {
            SpecialType.System_Boolean or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_Byte or
            SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64 or
            SpecialType.System_Decimal or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Char or
            SpecialType.System_String => true,
            _ => false
        };
    }
}