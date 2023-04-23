#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Collections.Immutable;
using System.Xml.Linq;

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

            string classCode = $@"namespace {nmspc};

public partial class {name}
{{
    {GetImplicitOperators(attrs, cls, model, usings)}
}}";
            return ($"{nmspc}{name}.mapper.g.cs", $@"//<auto generated>
#nullable enable{usings.Join(u => $@"
using {u};")}

{classCode}");

        }
        catch (Exception e)
        {
            return ($"{nmspc}{name}.mapper.g.cs", $"/*{e}*/");
        }

    }

    static string GetImplicitOperators(ImmutableArray<AttributeData> attrs, ITypeSymbol targetClass, SemanticModel model, HashSet<string> usings)
    {
        List<string> builders = new();
        return attrs.SelectMany(attr =>
            attr.AttributeClass!.TypeArguments
                .Select(sourceClass =>
                {
                    string sourceClassName = GetType(usings, sourceClass);
                    return $@"public static explicit operator {GetType(usings, targetClass)}({sourceClassName} from)
    {{
        return new {targetClass.Name} {{{GetInitializers(targetClass, sourceClass, model, builders, usings).Join(@",")}
        }}{builders.Join("")};
    }}";
                })).Join(@"
    
    ");
    }

    static IEnumerable<string> GetInitializers(ITypeSymbol target, ITypeSymbol source, SemanticModel model, List<string> code, HashSet<string> usings)
    {
        var sourceProperties = source.GetMembers().OfType<IPropertySymbol>().ToArray();
        foreach (var member in target.GetMembers())
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
                        //Mapper assignament
                        yield return @$"
	        {targetProp.Name} = {methodReference.Name}(from)";
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
                        //Mapper assignament
                        string assignment = @$"
	        {targetProp.Name} = ";

                        if (model.GetSymbolInfo(prop).Symbol is IPropertySymbol { Type: { } propType } &&
                                !AreSymbolsEquals(propType, targetPropType))
                            assignment += $"({GetType(usings, targetPropType)})";

                        assignment += $"from.{sourcePropName}";
                        
                        yield return assignment;
                        //Go to next property
                        goto exit;
                    }
                }

                if (sourceProperties.FirstOrDefault(s => s.Name == targetProp.Name) is { } sourceProp)
                {
                    var assignment = $"from.{sourceProp.Name}";

                    if (!AreSymbolsEquals(targetProp.Type, sourceProp.Type) && 
                        (!IsEnumerable(targetProp.Type) || !IsEnumerable(sourceProp.Type) || 
                        !ProcessEnumerableConversion(usings, targetProp, sourceProp, ref assignment))) 
                        
                        assignment = $"({GetType(usings, targetPropType)}){assignment}";

                    yield return $@"
	        {targetProp.Name} = {assignment}";

                }

            }
            else if (member is IMethodSymbol { Parameters: [{ Type: { } firstParamType }] } method &&
                AreSymbolsEquals(source, firstParamType) &&
                !method.ReturnsVoid &&
                AreSymbolsEquals(target, method.ReturnType)
            )
            {
                code.Add(@$"
            .{method.Name}(from)");
            }

        exit:;
        }
    }

    static bool ProcessEnumerableConversion(HashSet<string> usings, IPropertySymbol targetProp, IPropertySymbol sourceProp, ref string assignment)
    {        
        var nullCheck = targetProp.NullableAnnotation == NullableAnnotation.Annotated ? "?" : "";

        ITypeSymbol
            targetCollectionType = targetProp.Type is IArrayTypeSymbol arrSymb
                ? arrSymb.ElementType
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
        return attr.AttributeClass is { Name:{ } attrName, ContainingNamespace: { } nmspc } &&
            (attrName == $"{name}Attribute" || attrName == name) && 
            ((_namespace?.Length ?? 0) == 0 || nmspc.ToDisplayString() == _namespace) ;
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