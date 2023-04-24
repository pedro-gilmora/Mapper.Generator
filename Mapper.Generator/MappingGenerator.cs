#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Text;

namespace Mvvm.Extensions.Generator;

[Generator]
public class MappingGenerator : IIncrementalGenerator
{
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

            var code = new StringBuilder($@"namespace {nmspc};

public partial class {name}
{{
    ");
            GetImplicitOperators(code, attrs, cls, model, usings);

            code.Append(@"
}");
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

    static void GetImplicitOperators(StringBuilder code, ImmutableArray<AttributeData> attrs, ITypeSymbol targetClass, SemanticModel model, HashSet<string> usings)
    {
        List<string> builders = new();
        foreach (var attr in attrs)
        {
            var cls = attr.AttributeClass!.TypeArguments.First();
            string sourceClassName = GetType(usings, cls);
            code
                .AppendFormat(@"public static explicit operator {0}({1} from)
    {{
        return ",
                    GetType(usings, targetClass),
                    sourceClassName);

            if (UsesStaticMapper(attr, targetClass, cls, model, out var ee))

                code.Append($"{ee}(from);");

            else
            {
                code.Append($"new {targetClass.Name} {{");

                GetInitializers(code, targetClass, cls, model, builders, usings);

                code.Append(@"
        }");

                builders.Aggregate(code, (_, i) => code.Append(i)).Append(";");
            }

            code.Append(@"
    }");
        }
    }

    private static bool UsesStaticMapper(AttributeData attr, ITypeSymbol targetClass, ITypeSymbol cls, SemanticModel model,
        out string staticMapper)
    {
        staticMapper = null!;

        if (((AttributeSyntax)attr.ApplicationSyntaxReference?.GetSyntax()!).ArgumentList?.Arguments is not { } args)
            return false;

        foreach (var a in args)
        {
            if (a.Expression is not InvocationExpressionSyntax
                {   //Is nameof expression 
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{
                        // forces to match a member access sytntax
                        Expression: MemberAccessExpressionSyntax { Name: { } id } expr
                    }]
                }) continue;

            if (model.GetSymbolInfo(id) is not { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: { } cands })
                continue;

            foreach (var c in cands)
            {
                if (c is not IMethodSymbol { ReturnType: { } retType, Parameters: [{ Type: { } firstArgType }]} ||
                    !AreSymbolsEquals(retType, targetClass) || 
                    !AreSymbolsEquals(firstArgType, cls)) continue;

                staticMapper = expr.ToString();
                return true;
            }
        }
        return false;
    }

    static void GetInitializers(StringBuilder code, ITypeSymbol target, ITypeSymbol source, SemanticModel model, List<string> builders, HashSet<string> usings)
    {
        var inserted = false;
        var sourceProperties = GetAllSpearedDeclaratedMembers(source, model).OfType<IPropertySymbol>().ToArray();

        foreach (var member in GetAllSpearedDeclaratedMembers(target, model))
        {
            if (member is IPropertySymbol { Type: { } targetPropType } targetProp)
            {
                foreach (var atli in targetProp.GetAttributes())
                {
                    if (IgnoreProperty(atli))
                    {
                        //Skip property
                        goto exit;
                    }
                    else if (
                        AttributeHasName(atli, "MapWith", "Mapper.Generator.Attributes") &&
                        TryGetMethodReferenceFromNameOf(GetFirstParameter(atli), model, source, target, targetProp.Type, out var methodReference)
                    )
                    {
                        if (inserted)
                            code.Append(",");
                        else
                            inserted = true;

                        //Mapper assignament
                        code.Append(@$"
	        {targetProp.Name} = {methodReference.Name}(from)");
                        //Go to next property
                        goto exit;
                    }
                    else if (
                        AttributeHasName(atli, "MapFrom", "Mapper.Generator.Attributes") &&
                        GetFirstParameter(atli) is { } _nameOf &&
                        GetNameOfArgument(_nameOf, out var memberRef) &&
                        memberRef is MemberAccessExpressionSyntax { Name: { Identifier.Text: { } sourcePropName } prop } memberAccess &&
                        AreSymbolsEquals(model.GetSymbolInfo(memberAccess.Expression).Symbol, source)
                    )
                    {
                        if (inserted)
                            code.Append(",");
                        else
                            inserted = true;

                        //Mapper assignament
                        string assignment = @$"
	        {targetProp.Name} = ";

                        if (model.GetSymbolInfo(prop).Symbol is IPropertySymbol { Type: { } propType } &&
                                !AreSymbolsEquals(propType, targetPropType))
                            assignment += $"({GetType(usings, targetPropType)})";

                        assignment += $"from.{sourcePropName}";

                        code.Append(assignment);
                        //Go to next property
                        goto exit;
                    }
                }

                if (sourceProperties.FirstOrDefault(s => s.Name == targetProp.Name) is { } sourceProp)
                {
                    if (inserted)
                        code.Append(",");
                    else
                        inserted = true;

                    var assignment = $"from.{sourceProp.Name}";

                    if (!AreSymbolsEquals(targetProp.Type, sourceProp.Type) &&
                        (!IsEnumerable(targetProp.Type) || !IsEnumerable(sourceProp.Type) ||
                        !ProcessEnumerableConversion(usings, targetProp, sourceProp, ref assignment)))

                        assignment = $"({GetType(usings, targetPropType)}){assignment}";

                    code.Append($@"
	        {targetProp.Name} = {assignment}");

                }

            }
            else if (member is IMethodSymbol { Parameters: [{ Type: { } firstParamType }] } method &&
                AreSymbolsEquals(source, firstParamType) &&
                !method.ReturnsVoid &&
                AreSymbolsEquals(target, method.ReturnType)
            )
            {
                builders.Add(@$"
            .{method.Name}(from)");
            }

        exit:;
        }
    }

    static HashSet<ISymbol> GetAllSpearedDeclaratedMembers(ITypeSymbol target, SemanticModel model)
    {
        return new(
            target
                .DeclaringSyntaxReferences
                .SelectMany(r =>
                {
                    var source = r.GetSyntax();
                    ITypeSymbol test = ((ITypeSymbol)model.GetDeclaredSymbol(source)!);
                    return test?
                        .GetMembers()
                    ?? Enumerable.Empty<ISymbol>();
                }),
            SymbolEqualityComparer.Default);
    }

    static bool ProcessEnumerableConversion(HashSet<string> usings, IPropertySymbol targetProp, IPropertySymbol sourceProp, ref string assignment)
    {
        var nullCheck = targetProp.NullableAnnotation == NullableAnnotation.Annotated ? "?" : "";

        ITypeSymbol
            targetCollectionType = targetProp.Type is IArrayTypeSymbol { ElementType: { } eltype } arrSymb
                ? eltype
                : ((INamedTypeSymbol)targetProp.Type).TypeArguments[0],
            sourceCollectionType = sourceProp.Type is IArrayTypeSymbol arrSymb2
                ? arrSymb2.ElementType
                : ((INamedTypeSymbol)sourceProp.Type).TypeArguments[0];

        var targetCollectionTypeName = GetType(usings, targetCollectionType);

        if (!AreSymbolsEquals(targetCollectionType, sourceCollectionType))
        {
            assignment += $"{nullCheck}.Select(el => ({targetCollectionTypeName})el)";
        }
        if (targetProp.Type is IArrayTypeSymbol)
        {
            assignment += $"{nullCheck}.ToArray()";
        }
        else if (targetProp.Type.Name.EndsWith("List"))
        {
            assignment += $"{nullCheck}.ToList()";

            if (targetProp.Type.Name.StartsWith("IReadOnlyList"))
                assignment = $"({targetCollectionTypeName}){assignment}";
        }
        else if (targetProp.Type.Name.EndsWith("HashSet"))
        {
            assignment += $"{nullCheck}.ToHashSet()";
        }
        else
            return false;

        return true;
    }

    static bool IsEnumerable(ITypeSymbol type) => type.AllInterfaces.Any(ai => ai.SpecialType == SpecialType.System_Collections_IEnumerable);

    static ExpressionSyntax? GetFirstParameter(AttributeData atli)
    {
        return ((AttributeSyntax)atli.ApplicationSyntaxReference!.GetSyntax()).ArgumentList?.Arguments[0]?.Expression;
    }

    static bool TryGetMethodReferenceFromNameOf(ExpressionSyntax? arg0, SemanticModel model, ITypeSymbol source, ITypeSymbol target, ITypeSymbol propType, out IMethodSymbol methodReference)
    {
        methodReference = default!;
        if (GetNameOfArgument(arg0, out var nameOfArg))
            foreach (var m in model.GetMemberGroup(nameOfArg))
            {
                if (m is IMethodSymbol { Parameters: [{ Type: { } srcType }], ReturnType: { } retType } mm &&
                    AreSymbolsEquals(source, srcType))
                {
                    methodReference = mm;
                    return true;
                }
            }
        return false;
    }

    static bool GetNameOfArgument(ExpressionSyntax? arg0, out ExpressionSyntax expr)
    {
        expr = null!;

        return arg0 is InvocationExpressionSyntax { ArgumentList.Arguments: [{ Expression: { } paramValue }], Expression: { } name } &&
            name.ToString() == "nameof" &&
            (expr = paramValue) != null;
    }

    private static bool IgnoreProperty(AttributeData attr) => AttributeHasName(attr, "IgnoreMap");

    private static bool AttributeHasName(AttributeData attr, string name, string _namespace = "")
    {
        return attr.AttributeClass is { Name: { } attrName, ContainingNamespace: { } nmspc } &&
            (attrName == $"{name}Attribute" || attrName == name) &&
            ((_namespace?.Length ?? 0) == 0 || nmspc.ToDisplayString() == _namespace);
    }

    private static string GetType(HashSet<string> usings, ITypeSymbol type)
    {
        RegisterNamespace(usings, type.ContainingNamespace.ToString());

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

    private static void RegisterNamespace(HashSet<string> usings, params string[] namespaces)
    {
        foreach (var ns in namespaces)
            if (ns != "<global namespace>")
                usings.Add(ns);
    }

    private static bool IsPrimitive(INamedTypeSymbol type)
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