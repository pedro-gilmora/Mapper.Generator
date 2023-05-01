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
using SourceCrafter.Mapping.Constants;
using SourceCrafter.Mapping.Extensions;

[assembly: InternalsVisibleTo("SourceCrafter.MappingGenerator.UnitTests")]
namespace SourceCrafter;

[Generator]
public class MappingGenerator : IIncrementalGenerator
{
    public const string DiagnosticId = "NotAllowedIgnoreAndMap";

    private static readonly LocalizableString Title = "Two attributes on the same property";
    private static readonly LocalizableString MessageFormat = "Property '{0}' has both, Ignore and Map attributes";
    private static readonly LocalizableString Description = "Ignore and Map attributes attributes should not be present on the same property.";
    private const string Category = "Naming";

#pragma warning disable IDE0052, RE2008 // Quitar miembros privados no leídos
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);
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
                    "SourceCrafter.Mapping.Attributes.MapAttribute`1",
                    static (n, _) => n is ClassDeclarationSyntax or RecordDeclarationSyntax or InterfaceDeclarationSyntax,
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
                : "SourceCrafter";
            var code = new StringBuilder($@"namespace {_namespace};

public static partial class GlobalMappers {{
    ");

            foreach (var item in attributes)
            {
                var ignore = Ignore.None;

                if (item.AttributeClass is { MetadataName: "MapAttribute`2", TypeArguments: [{ } fromType, { } toType] })
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

                    var semanticModel = fromType.DeclaringSyntaxReferences
                        .Select(a => compilation.GetSemanticModel(a.SyntaxTree))
                        .FirstOrDefault(a => a != null)!;

                    BuildConverters(code, fromType, toType, toMapper, fromMapper, semanticModel, false, builders, item, ignore
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
                            m.ReturnType.ToDisplayString() == to.ToDisplayString() &&
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

    static void CreateRelatedTypeFiles(SourceProductionContext sourceProducer, SemanticModel model, ITypeSymbol _class, ImmutableArray<AttributeData> attrs)
    {
        HashSet<string> usings = new();
        string
            name = _class.Name,
            nmspc = _class.ContainingNamespace.ToDisplayString();

        try
        {
#if DEBUG
            string extraText = "";
#endif
            var code = new StringBuilder($@"namespace {nmspc};

public partial class {GetImplementationClassName(_class)}
{{
    ");
            var len = code.Length;
            GetConverters(code, attrs, _class, model, usings
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
            var ignore = GetIgnoreValue(attr.ConstructorArguments.ElementAtOrDefault(2));



            if (attr.AttributeClass is not { MetadataName: "MapAttribute`1" } || ignore is Ignore.Both)
                continue;

            var toClass = attr.AttributeClass!.TypeArguments.First();
#if DEBUG
            extraText += $@"
Map<{toClass}>.Ignore: {ignore}";
#endif

            TryResolveMappers(model.Compilation, attr, fromClass, toClass, out var fromMapper, out var toMapper);
            BuildConverters(code, fromClass, toClass, toMapper, fromMapper, model, isOperator, builders, attr, ignore
#if DEBUG
        , ref extraText
#endif
        );
        }
    }

    internal static void BuildConverters(
        StringBuilder code,
        ITypeSymbol fromType,
        ITypeSymbol toType,
        IMethodSymbol? toMapper,
        IMethodSymbol? fromMapper,
        SemanticModel model,
        bool isClassAttribute,
        List<string> builders,
        AttributeData attr,
        Ignore ignore
#if DEBUG
, ref string extraText
#endif
    )
    {
        var fromMetaName = fromType.MetadataName.Replace('`', '_');
        var toMetaName = toType.MetadataName.Replace('`', '_');
        var fromTypeFullName = GetType(fromType);
        var toTypeFullName = GetType(toType);
        Action<StringBuilder>? propsBuilder = null;
        Action<StringBuilder>? reversePropsBuilder = null;
        var appendedProperty = false;
#if DEBUG
        extraText += $@"
From {GetType(fromType, true)} to {GetType(toType, true)}";
#endif

        if (ignore != Ignore.OnTarget)
        {
            GetInitializers(fromType, toType, model, ref propsBuilder, ref reversePropsBuilder, InsertComma, ignore
#if DEBUG
                , ref extraText
#endif
            );
            code
                .Append($@"
    public static {(isClassAttribute
                        ? $"explicit operator {GetType(fromType, true)}("
                        : $"{fromTypeFullName} To{fromMetaName}(this ")}{(isClassAttribute ? GetType(toType, true) : toTypeFullName)} from)
    {{
        return ");


            if (toMapper != null)
            {
                code.Append($"{GetStaticMethodName(toMapper)}(from);");
            }
            else
            {
                code.Append($"new {GetType(fromType, true)} {{");

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
                GetInitializers(fromType, toType, model, ref propsBuilder, ref reversePropsBuilder, InsertComma, ignore
#if DEBUG
                    , ref extraText
#endif
                );

            code
                .Append($@"
    public static {(isClassAttribute
                        ? $"explicit operator {(isClassAttribute ? GetType(toType, true) : toTypeFullName)}("
                        : $"{toTypeFullName} To{toMetaName}(this ")}{(isClassAttribute ? GetType(fromType, true) : fromTypeFullName)} from)
    {{
        return ");

            if (fromMapper != null)

                code.Append($"{GetStaticMethodName(fromMapper)}(from);");

            else
            {
                code.Append($"new {GetType(toType, true)} {{");
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

    private static object GetStaticMethodName(IMethodSymbol mapper)
    {
        return $"{GetType(mapper.ContainingType, true)}.{mapper.Name}";
    }

    private static Ignore GetIgnoreValue(TypedConstant possibleSecondArgument) =>
        possibleSecondArgument is { Value: int mapValue }
                    ? (Ignore)mapValue
                    : Ignore.None;

    static void GetInitializers(
        ITypeSymbol fromClass,
        ITypeSymbol toClass,
        SemanticModel model,
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
                    buildMap += (StringBuilder sb) => BuildPropertyMap(fromMember, toMember, sb.Append, appendComma);

                if (parentIgnore != Ignore.OnSource && ignore != Ignore.OnSource)
                    buildReverseMap += (StringBuilder sb) => BuildPropertyMap(toMember, fromMember, sb.Append, appendComma);
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
            if (ignoreItems > 0)
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
        ISymbol toMember,
        ISymbol fromMember,
        Func<string, StringBuilder> append,
        Action appendComma)
    {
        if (toMember is IPropertySymbol { IsReadOnly: true } || fromMember is IPropertySymbol { IsWriteOnly: true })
            return;

        appendComma();

        var valueExpression = $"from.{fromMember.Name}";
        var fromMemberType = fromMember is IFieldSymbol fldMap
            ? fldMap.Type
            : ((IPropertySymbol)fromMember).Type;
        var toMemberType = toMember is IFieldSymbol fld
            ? fld.Type
            : ((IPropertySymbol)toMember).Type;

        if (!AreSymbolsEquals(toMemberType, fromMemberType) &&
            (!IsEnumerable(toMemberType) || !IsEnumerable(fromMemberType) ||
            !ProcessEnumerableConversion(toMemberType, fromMemberType, ref valueExpression)))

            valueExpression = GetConversion(valueExpression, toMemberType, fromMemberType);

        append($@"
	        {toMember.Name} = {valueExpression}");
    }

    private static string GetConversion(string expression, ITypeSymbol toType, ITypeSymbol fromType)
    {
        var typeName = GetType(toType);
        if (typeName[^1] != '?' && fromType.NullableAnnotation == NullableAnnotation.Annotated)
            expression += "!";
        return $"({typeName}){expression}";
    }

    private static bool IsNotNullable(ITypeSymbol fromType)
    {
        return fromType.NullableAnnotation != NullableAnnotation.Annotated && (fromType.IsValueType || fromType.IsTupleType);
    }

    static bool ProcessEnumerableConversion(
        ITypeSymbol toType,
        ITypeSymbol fromType,
        ref string valueExpression)
    {
        var nullable = toType.NullableAnnotation == NullableAnnotation.Annotated ? "?" : "";

        ITypeSymbol
            toCollectionType = toType is IArrayTypeSymbol { ElementType: { } eltype } arrSymb
                ? eltype
                : ((INamedTypeSymbol)toType).TypeArguments[0],
            fromCollectionType = fromType is IArrayTypeSymbol arrSymb2
                ? arrSymb2.ElementType
                : ((INamedTypeSymbol)fromType).TypeArguments[0];

        var toCollectionTypeName = GetType(toCollectionType);

        if (!AreSymbolsEquals(toCollectionType, fromCollectionType))
        {
            valueExpression += $"{nullable}.Select(el => {GetConversion("el", toCollectionType, fromCollectionType)})";
        }
        if (toType is IArrayTypeSymbol)
        {
            valueExpression += $"{nullable}.ToArray()";
        }
        else if (toType.Name.EndsWith("List"))
        {
            valueExpression += $"{nullable}.ToList()";

            if (toType.Name.StartsWith("IReadOnlyList"))
                valueExpression = $"({toCollectionTypeName}{nullable}){valueExpression}";
        }
        else if (toType.Name.EndsWith("HashSet"))
        {
            valueExpression += $"{nullable}.ToHashSet()";
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

    static string GetType(ITypeSymbol type, bool useInterfaceImplName = false)
    {
        return type switch
        {

            IArrayTypeSymbol { ElementType: { } arrayType }
                => GetType(arrayType) + "[]",

            INamedTypeSymbol { Name: "Nullable", TypeArguments: [{ } underlyingType] }
                => $"{GetType(underlyingType)}?",

            INamedTypeSymbol { IsTupleType: true, TupleElements: var elements }
                => $"({elements.Join(f => $"{GetType(f.Type)}{(f.IsExplicitlyNamedTupleElement ? $" {f.Name}" : "")}", ", ")})",

            INamedTypeSymbol { Name: var name, TypeArguments: { Length: > 0 } generics }
                => GetPossibleNamsepace(type) + $"{name}<{generics.Join(g => GetType(g), ", ")}>",
            _
                => GetTypeName(type, useInterfaceImplName)
        };
    }

    private static string GetPossibleNamsepace(ITypeSymbol type)
    {
        return type.ContainingNamespace?.ToString() is { Length: > 0 } nmspc
                    ? $"{nmspc}."
                    : "";
    }

    private static string GetTypeName(ITypeSymbol type, bool useInterfaceImplName)
    {
        var isPrimitive = IsPrimitive((INamedTypeSymbol)type);
        var typeName = isPrimitive ? type.ToDisplayString() : type.Name;

        if (useInterfaceImplName && typeName[0] == 'I' && type.TypeKind == TypeKind.Interface)
            typeName = typeName[1..];

        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            typeName += "?";

        return isPrimitive ? typeName : GetPossibleNamsepace(type) + typeName;
    }

    private static string GetImplementationClassName(ITypeSymbol type)
    {
        var isPrimitive = IsPrimitive((INamedTypeSymbol)type);
        var typeName = isPrimitive ? type.ToDisplayString() : type.Name;

        return (typeName[0] == 'I' && type.TypeKind == TypeKind.Interface) 
            ? typeName[1..]
            :typeName;
    }

    static bool IsPrimitive(INamedTypeSymbol? type)
    {
        return type?.SpecialType is
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
            SpecialType.System_String;
    }
}