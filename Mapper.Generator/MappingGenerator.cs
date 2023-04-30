#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RogueGen.Mapping.Constants;
using RogueGen.Mapping.Extensions;

[assembly: InternalsVisibleTo("Mapper.Generator.UnitTests")]
namespace RogueGen;

[Generator]
public class MappingGenerator : IIncrementalGenerator
{
    public const string DiagnosticId = "NotAllowedIgnoreAndMap";

    private static readonly LocalizableString Title = "Two attributes on the same property";
    private static readonly LocalizableString MessageFormat = "Property '{0}' has both, Ignore and Map attributes";
    private static readonly LocalizableString Description = "Ignore and Map attributes attributes should not be present on the same property.";
    private const string Category = "Naming";

#pragma warning disable IDE0052, RE2008 // Quitar miembros privados no leídos
    private static readonly DiagnosticDescriptor Rule = new (DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);
#pragma warning restore IDE0052, RE2008 // Quitar miembros privados no leídos

    static readonly object _lock = new();
    static readonly Func<ISymbol?, ISymbol?, bool> AreSymbolsEquals = SymbolEqualityComparer.Default.Equals;
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        lock (_lock)
        {
            context.RegisterImplementationSourceOutput(
                context.CompilationProvider,
                ProcessAssemblyAttributes
            );

            context.RegisterImplementationSourceOutput(
                //Collect classes with metadata
                context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "RogueGen.Mapping.Attributes.MapAttribute`1",
                    static (n, _) => n is ClassDeclarationSyntax or RecordDeclarationSyntax,
                    static (ctx, c) => (ctx.Attributes, TypeSymbol: (ITypeSymbol)ctx.TargetSymbol, ctx.SemanticModel)
                ),
                static (sourceProducer, interfaceToGenerate) =>
                    CreateRelatedTypeFiles(
                        sourceProducer,
                        interfaceToGenerate.SemanticModel,
                        interfaceToGenerate.TypeSymbol,
                        interfaceToGenerate.Attributes));
        }
    }

    private static void ProcessAssemblyAttributes(SourceProductionContext context, Compilation compilation)
    {
        try
        {
            var assembly = compilation.Assembly;
#if DEBUG
            var extraText = "";
#endif
            var attributes = assembly.GetAttributes();
            var mappersCount = 0;

            var _namespace = (assembly.GlobalNamespace.ToDisplayString() is { } ns and not "<global namespace>")
                ? ns
                : "RogueGen";
            var code = new StringBuilder($@"namespace {_namespace};

public static partial class GlobalMappers {{
    ");

            foreach (var item in attributes)
            {
                var ignore = Ignore.None;

                if (item.AttributeClass is { MetadataName: "MapAttribute`2", TypeArguments: [{ } fromType, { }toType] })
                {
                    if (item.ConstructorArguments[2].Value is int value)
                        ignore = (Ignore)value;

                    TryResolveMappers(compilation, item, fromType, toType, out var fromMapper, out var toMapper);
#if DEBUG
                    extraText += $@"
fromMapper: {fromMapper}
toMapper: {toMapper}";
#endif
                    if (mappersCount > 0)
                        code.Append(@"
    ");
                    mappersCount++;
                    

                    List<string> builders = new();

                    BuildConverters(code, fromType, toType, toMapper, fromMapper, fromType.DeclaringSyntaxReferences.Select(a => compilation.GetSemanticModel(a.SyntaxTree)).FirstOrDefault(a => a != null)!, new(), false, builders, item, ignore
#if DEBUG
                        , ref extraText
#endif
                    );
                }

            }

            if (mappersCount == 0) 
                return;

            code.Append(@"
}");
#if DEBUG
            code.Append($@"
/*
Extras:
-------{extraText}
*/
");
#endif
            context.AddSource("GlobalMappers.Generated.cs", code.ToString());
        }
        catch (Exception e)
        {
            context.AddSource("GlobalMappers.Generated.cs", $"/*{e}*/");
        }
    }

    internal static void TryResolveMappers(Compilation compilation, AttributeData item, ITypeSymbol fromType, ITypeSymbol toType, out IMethodSymbol? fromMapper, out IMethodSymbol? toMapper)
    {
        fromMapper = toMapper = null;
        if (item.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attr &&
            attr.ArgumentList?.Arguments is { Count: { } count } args)
        {
            for (int i = 0; i < count; i++)
            {
                if (args[i] is { 
                    Expression: InvocationExpressionSyntax { 
                        ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }], 
                        Expression: SimpleNameSyntax { Identifier.Text: "nameof" } } method 
                } arg)
                {
                    switch ((i, arg.NameColon?.Name.Identifier.Text ?? arg.NameEquals?.Name.Identifier.Text))
                    {
                        case (0, null) or (_, "fromMapper"):
                            TryFindMapperMethod(compilation.GetSemanticModel(id.SyntaxTree), id, fromType, toType, out fromMapper);
                            break;
                        case (1, null) or (_, "toMapper"):
                            TryFindMapperMethod(compilation.GetSemanticModel(id.SyntaxTree), id, fromType, toType, out toMapper);
                            break;
                    }
                }
            }
        }
    }

    private static bool TryFindMapperMethod(SemanticModel model, SyntaxNode id, ISymbol from, ISymbol to, out IMethodSymbol mapper)
    {
        return null != (mapper = model.GetSymbolInfo(id) switch
        {
            { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: { Length: > 0 } candidates }
                when candidates
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault(m =>
#if DEBUG
                            m.ReturnType.ToDisplayString() ==  to.ToDisplayString() &&
                            m.Parameters.FirstOrDefault()?.Type?.ToDisplayString() == from.ToDisplayString()
#else
                            AreSymbolsEquals(m.ReturnType, to) &&
                            AreSymbolsEquals(m.Parameters.FirstOrDefault()?.Type, from)
#endif
                        ) is { } foundCandidate =>
                foundCandidate,

            _ => default!
        });
    }

    static void CreateRelatedTypeFiles(SourceProductionContext sourceProducer, SemanticModel model, ITypeSymbol cls, ImmutableArray<AttributeData> attrs)
    {
        HashSet<string> usings = new();
        string
            name = cls.Name,
            nmspc = cls.ContainingNamespace.ToDisplayString();

        try
        {
#if DEBUG
            string extraText = "";
#endif
            var code = new StringBuilder($@"namespace {nmspc};

public partial class {name}
{{
    ");
            var len = code.Length;
            GetConverters(code, attrs, cls, model, usings
#if DEBUG
                , ref extraText
#endif
            );
            if (code.Length == len)
                return;

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
            sourceProducer.AddSource($"{nmspc}.{name}.mapper.g.cs", $@"//<auto generated>
#nullable enable{usings.Join(u => $@"
using {u};")}

{code}");

        }
        catch (Exception e)
        {
            sourceProducer.AddSource($"{nmspc}.{name}.mapper.g.cs", $"/*{e}*/");
        }

    }

    internal static void GetConverters(StringBuilder code, ImmutableArray<AttributeData> attrs, ITypeSymbol fromClass, SemanticModel model, HashSet<string> usings
#if DEBUG
        , ref string extraText
#endif
        , bool isOperator = true)
    {
        List<string> builders = new();
        foreach (var attr in attrs.AsSpan())
        {
            var ignore = GetIgnore(attr.ConstructorArguments.ElementAtOrDefault(2));



            if (attr.AttributeClass is not { MetadataName: "MapAttribute`1" } || ignore is Ignore.Both)
                continue;

            var toClass = attr.AttributeClass!.TypeArguments.First();
#if DEBUG
            extraText += $@"
Map<{toClass}>.Ignore: {ignore}";
#endif

            TryResolveMappers(model.Compilation, attr, fromClass, toClass, out var fromMapper, out var toMapper);
            BuildConverters(code, fromClass, toClass, toMapper, fromMapper, model, usings, isOperator, builders, attr, ignore
#if DEBUG
        , ref extraText
#endif
        );
        }
    }

    internal static void BuildConverters(
        StringBuilder code, 
        ITypeSymbol fromClass, 
        ITypeSymbol toClass, 
        IMethodSymbol? toMapper, 
        IMethodSymbol? fromMapper, 
        SemanticModel model,
        HashSet<string> usings, 
        bool isClassAttribute, 
        List<string> builders, 
        AttributeData attr,
        Ignore ignore
#if DEBUG
, ref string extraText
#endif
    )
    {
        var fromMetaName = fromClass.MetadataName.Replace('`', '_');
        var toMetaName = toClass.MetadataName.Replace('`', '_');
        Action<StringBuilder>? propsBuilder = null;
        Action<StringBuilder>? reversePropsBuilder = null;
        var appendedProperty = false;

        if (ignore != Ignore.OnTarget)
        {
            GetInitializers(fromClass, toClass, model, usings, ref propsBuilder, ref reversePropsBuilder, InsertComma, ignore
#if DEBUG
                , ref extraText
#endif
            );
            code
                .Append($@"
    public static {(isClassAttribute
                        ? $"explicit operator {fromClass}("
                        : $"{fromClass} To{fromMetaName}(this ")}{toClass} from)
    {{
        return ");


            if (toMapper != null)
            {
                code.Append($"{GetStaticMethodName(toMapper)}(from);");
            }
            else
            {

                code.Append($"new {fromClass} {{");

                propsBuilder?.Invoke(code);

                code.Append(@"
        };");
            }

            code.Append(@"
    }");
        }

        builders.Clear();

        if (ignore != Ignore.OnSource)
        {
            if (ignore == Ignore.OnTarget)
                GetInitializers(fromClass, toClass, model, usings, ref propsBuilder, ref reversePropsBuilder, InsertComma, ignore
#if DEBUG
                    , ref extraText
#endif
                );

            code
                .Append($@"
    public static {(isClassAttribute
                        ? $"explicit operator {toClass}("
                        : $"{toClass} To{toMetaName}(this ")}{fromClass} from)
    {{
        return ");

            if (fromMapper != null)

                code.Append($"{GetStaticMethodName(fromMapper)}(from);");

            else
            {
                code.Append($"new {toClass} {{");
                appendedProperty = false;
                reversePropsBuilder?.Invoke(code);

                code.Append(@"
        };");
            }

            code.Append(@"
    }");
        }

        void InsertComma()
        {
            if (appendedProperty)
                code.Append(",");
            else
                appendedProperty = true;
        }
    }

    private static object GetStaticMethodName(IMethodSymbol fromMapper)
    {
        return $"{GetType(fromMapper.ContainingType, true)}.{fromMapper.Name}";
    }

    private static Ignore GetIgnore(TypedConstant possibleSecondArgument) =>
        possibleSecondArgument is { Value: int mapValue }
                    ? (Ignore)mapValue
                    : Ignore.None;

    static void GetInitializers(
        ITypeSymbol fromClass, 
        ITypeSymbol toClass, 
        SemanticModel model,
        HashSet<string> usings,  
        ref Action<StringBuilder>? buildMap,
        ref Action<StringBuilder>? buildReverseMap, 
        Action appendComma,
        Ignore parentIgnore
#if DEBUG
        , ref string extraText
#endif
    )
    {
        var toMembers = GetMappableMembers(toClass).ToImmutableArray().AsSpan();

#if DEBUG
        var fromMembers = GetMappableMembers(fromClass).ToImmutableArray().AsSpan();
        extraText += $@"
{fromClass.Name} members count: {fromMembers.Length}
{toClass.Name} members count: {toMembers.Length}";

        foreach (var fromMember in fromMembers)
        {
#else
        foreach (var fromMember in GetMappableMembers(fromClass).ToImmutableArray().AsSpan())
        {
#endif
            if ((TryMapFromAttribute(fromMember, toClass, model, out Ignore ignore, out var toMember
#if DEBUG
                , ref extraText
#endif
                ) ||
                TryMapFromRight(toMembers, fromMember, out toMember)) &&
                ignore != Ignore.Both
                )
            {
                if (parentIgnore != Ignore.OnTarget && ignore != Ignore.OnTarget)
                    buildMap += (StringBuilder sb) => BuildPropertyMap(usings, fromMember, toMember, sb.Append, appendComma);

                if (parentIgnore != Ignore.OnSource && ignore != Ignore.OnSource)
                    buildReverseMap += (StringBuilder sb) => BuildPropertyMap(usings, toMember, fromMember, sb.Append, appendComma);
            }
        }
    }

    static IEnumerable<ISymbol> GetMappableMembers(ITypeSymbol? target) =>
            target == null || IsObjectOrPrimitive(target)
            ? Enumerable.Empty<ISymbol>()
            : GetMappableMembers(target.BaseType)
                .Concat(target.GetMembers()
                .Where(member => member is IPropertySymbol or IFieldSymbol { AssociatedSymbol: null })
                .Distinct(SymbolEqualityComparer.Default));

    private static bool IsObjectOrPrimitive(ITypeSymbol target)
    {
        return IsPrimitive(target as INamedTypeSymbol) || target.SpecialType == SpecialType.System_Object;
    }

    static bool TryMapFromAttribute(
        ISymbol prop, ITypeSymbol toClass, SemanticModel model,
        out Ignore ignore,
        out ISymbol element
#if DEBUG
        , ref string extraText
#endif
    )
    {
        element = null!;
        ignore = 0;
        byte ignoreItems = 0;

        foreach (var (attrName, firstArg, ignore2) in prop.GetAttributes().Select(GetAttrInfo).ToImmutableArray().AsSpan())
        {
            if (attrName != "Map")
            {
                if (ignore2 > ignore)
                {
                    ignore = ignore2;
                    ignoreItems++;
                }
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
                !AreSymbolsEquals(memberType, toClass) ||
                (element = model.GetSymbolInfo(id).Symbol!) is not (IPropertySymbol or IFieldSymbol { AssociatedSymbol: null })
            )
                continue;


            if (ignore2 > ignore)
            {
                ignore = ignore2;
                ignoreItems++;
            }
            if(ignoreItems > 0)
            {

            }
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
        if (attr.AttributeClass?.Name is not ("MapAttribute" or "IgnoreAttribute"))
            return (attr.AttributeClass?.Name!, null, Ignore.None);

        var args = (attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments;
        var name = attr.AttributeClass!.Name!.Replace("Attribute", "");

        return (name, args, attr.ConstructorArguments) switch
        {
            ("Map", [{ Expression: { } expr }, ..], var ctorArgs) => (
                name, expr, ctorArgs is [_, { Value: int mapValue }] && (Ignore)mapValue is { } val and not Ignore.Both ? val : Ignore.None),

            ("Ignore", _, var ctorArgs) => (
                name,
                null,
                ctorArgs is [{ Value: int mapValue }]
                    ? (Ignore)mapValue
                    : Ignore.Both),

            _ => (name, null, Ignore.None)
        };
    }
    static void BuildPropertyMap(
        HashSet<string> usings, 
        ISymbol fromMember, 
        ISymbol toMember, 
        Func<string, StringBuilder> append, 
        Action appendComma)
    {
        if (fromMember is IPropertySymbol { IsReadOnly: true } || toMember is IPropertySymbol { IsWriteOnly: true })
            return;

        appendComma();

        var assignment = $"from.{toMember.Name}";
        var mapMemberType = toMember is IFieldSymbol fldMap
            ? fldMap.Type
            : ((IPropertySymbol)toMember).Type;
        var memberType = fromMember is IFieldSymbol fld
            ? fld.Type
            : ((IPropertySymbol)fromMember).Type;

        if (!AreSymbolsEquals(memberType, mapMemberType) &&
            (!IsEnumerable(memberType) || !IsEnumerable(mapMemberType) ||
            !ProcessEnumerableConversion(usings, memberType, mapMemberType, memberType.NullableAnnotation == NullableAnnotation.Annotated, ref assignment)))

            assignment = $"({GetType(usings, memberType)}){assignment}";

        append($@"
	        {fromMember.Name} = {assignment}");
    }

    static bool ProcessEnumerableConversion(
        HashSet<string> usings, 
        ITypeSymbol targetType, 
        ITypeSymbol mapType, 
        bool isNullable, 
        ref string assignment)
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
#if DEBUG
        return type is IArrayTypeSymbol || type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" ||
            type.MetadataName.Contains("List") || type.MetadataName.Contains("Set`") ||
            type.MetadataName.Contains("Dictionary") || type.MetadataName.Contains("Array");
#else
        if (type.SpecialType == SpecialType.System_String)
            return false;
        foreach (var ai in type.AllInterfaces)
            if (ai.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
        return false;
#endif
    }

    static bool TryMapFromRight(
        ReadOnlySpan<ISymbol> 
        toMembers, 
        ISymbol fromMember, 
        out ISymbol toMember)
    {
        toMember = null!;

        foreach (var member in toMembers)
        {
            if (member is not IPropertySymbol and not IFieldSymbol || member.Name != fromMember.Name)
                continue;

            toMember = member;
            return true;
        }

        return false;
    }

    static string GetType(HashSet<string> usings, ITypeSymbol type, bool useNamespace = false)
    {
        var possibleNamsepace = "";
        if (useNamespace && type.ContainingNamespace?.ToString() is { } nmspc)
        {
            RegisterNamespace(usings, possibleNamsepace = nmspc);
            possibleNamsepace += ".";
        }

        return possibleNamsepace + type switch
        {
            INamedTypeSymbol { Name: "Nullable", TypeArguments: [{ } underlyingType] }
                => $"{GetType(usings, underlyingType)}?",

            INamedTypeSymbol { IsTupleType: true, TupleElements: var elements }
                => $"({elements.Join(f => $"{GetType(usings, f.Type)}{(f.IsExplicitlyNamedTupleElement ? $" {f.Name}" : "")}", ", ")})",

            INamedTypeSymbol { Name: var name, TypeArguments: { Length: > 0 } generics }
                => $"{name}<{generics.Join(g => GetType(usings, g), ", ")}>",

            IArrayTypeSymbol { ElementType: { } arrayType }
                => GetType(usings, arrayType) + "[]",
            _
                => IsPrimitive((INamedTypeSymbol)type) ? type.ToDisplayString() : type.Name
        };
    }

    static string GetType(ITypeSymbol type, bool useNamespace = false)
    {
        var possibleNamsepace = useNamespace && type.ContainingNamespace?.ToString() is { Length: > 0 } nmspc
            ? $"{nmspc}."
            : "";

        return possibleNamsepace + type switch
        {
            INamedTypeSymbol { Name: "Nullable", TypeArguments: [{ } underlyingType] }
                => $"{GetType(underlyingType, useNamespace)}?",

            INamedTypeSymbol { IsTupleType: true, TupleElements: var elements }
                => $"({elements.Join(f => $"{GetType(f.Type, useNamespace)}{(f.IsExplicitlyNamedTupleElement ? $" {f.Name}" : "")}", ", ")})",

            INamedTypeSymbol { Name: var name, TypeArguments: { Length: > 0 } generics }
                => $"{name}<{generics.Join(g => GetType(g, useNamespace), ", ")}>",

            IArrayTypeSymbol { ElementType: { } arrayType }
                => GetType(arrayType, useNamespace) + "[]",
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

    static bool IsPrimitive(INamedTypeSymbol? type)
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