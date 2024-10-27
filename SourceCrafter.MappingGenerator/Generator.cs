using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System;
using SourceCrafter.Bindings.Constants;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

[assembly: InternalsVisibleTo("SourceCrafter.Bindings.UnitTests")]
namespace SourceCrafter.Bindings;

[Generator]
public class Generator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        try
        {
#if DEBUG_SG
            Debugger.Launch();
#endif
            var d = FindMapperAttributes(context,
                "SourceCrafter.Bindings.Attributes.ExtendAttribute`1",
                static n => true,
                static (targetSymbol, model, attr) => attr.AttributeClass?.TypeArguments[0]!)
                .Combine(
                    FindMapperAttributes(context,
                    "SourceCrafter.Bindings.Attributes.ExtendAttribute",
                    static n => n is EnumDeclarationSyntax,
                    static (targetSymbol, model, attr) => (ITypeSymbol)targetSymbol)
                    .Combine(
                        FindMapperAttributes(context,
                            "SourceCrafter.Bindings.Attributes.BindAttribute`1",
                            static n => n is ClassDeclarationSyntax,
                            static (targetSymbol, model, attr) =>
                                attr is { AttributeClass.TypeArguments: [{ } target], ConstructorArguments: [{ Value: int mapKind }, { Value: int ignore }, ..] }
                                    ? new MapInfo(
                                            (ITypeSymbol)targetSymbol,
                                            target,
                                            (MappingKind)mapKind,
                                            (ApplyOn)ignore)
                                    : default)
                        .Combine(
                            FindMapperAttributes(
                                context,
                                "SourceCrafter.Bindings.Attributes.BindAttribute`2",
                                static n => n is CompilationUnitSyntax,
                                static (_, model, attr) =>
                                    attr is { AttributeClass.TypeArguments: [{ } target, { } source], ConstructorArguments: [{ Value: int mapKind }, { Value: int ignore }, ..] }
                                        ? new MapInfo(
                                            target,
                                            source,
                                            (MappingKind)mapKind,
                                            (ApplyOn)ignore)
                                        : default))));

            context.RegisterSourceOutput(context.CompilationProvider.Combine(d), (ctx, info)
                =>
            {
                var (compilation, (enums, (enums2, (attrs, classes)))) = info;

                ProcessPosibleMappers(
                    ctx,
                    compilation,
                    enums.Concat(enums2).Distinct(SymbolEqualityComparer.Default).Cast<ITypeSymbol>().ToImmutableArray(),
                    attrs,
                    classes);
            });

        }
        catch (Exception e)
        {
            Trace.Write("[SourceCrafter Exception]" + e.ToString());
        }
    }

    private void ProcessPosibleMappers(SourceProductionContext ctx, Compilation compilation, ImmutableArray<ITypeSymbol> enums, ImmutableArray<MapInfo> OverClass, ImmutableArray<MapInfo> Assembly)
    {
        try
        {
            BuildCode(ctx.AddSource, compilation, enums, OverClass, Assembly);
        }
        catch (Exception e)
        {
            if (Debugger.IsAttached && Debugger.IsLogging())
                Debugger.Log(0, "SGExceptions", "[SouceCrafter.Bindings]: Error attempting to generate mappings and enum extensions:\n" + e.ToString());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void BuildCode(
        Action<string, string> addSource,
        Compilation compilation,
        ImmutableArray<ITypeSymbol> enums,
        ImmutableArray<MapInfo> classesLevel,
        ImmutableArray<MapInfo> assembliesLevel)
    {
        TypeSet typeSet = new(compilation);

        SortedSet<string> enumPropertyNameSet = new(StringComparer.OrdinalIgnoreCase);

        var set = new MappingSet(compilation, typeSet);

        set.Initialize(0);

        bool generatedLock = false;

        foreach (var enumType in enums)
        {
            var type = typeSet.GetOrAdd(enumType);

            StringBuilder code = new();
            var len = code.Length;

            type.BuildEnumMethods(code);

            if (len == code.Length) return;

            const string head = @"#nullable enable
namespace SourceCrafter.EnumExtensions;

public static partial class EnumExtensions
{";
            var propertyName = BuildProperty(type, enumPropertyNameSet);

            code.Insert(0, head);

            code.Insert(head.Length, @$"
    public const {type.NotNullFullName} {propertyName}Enum = default;
");

            if (!generatedLock)
            {
                generatedLock = true;

                code.Insert(head.Length, @"
    private static object? __lock = new();");
            }

            code.Append(@"
}");
            addSource(type.ExportFullName.Replace("global::", ""), code.ToString());


        }

        foreach (var gctx in assembliesLevel)
            if (gctx.Generate)
                set.AddMapper(gctx.From, gctx.To, gctx.Ignore, gctx.MapKind, addSource);

        foreach (var gctx in classesLevel)
            if (gctx.Generate)
                set.AddMapper(gctx.From, gctx.To, gctx.Ignore, gctx.MapKind, addSource);
    }

    private string BuildProperty(TypeMetadata type, SortedSet<string> enumPropertyNameSet)
    {
        if (enumPropertyNameSet.Add(type.SanitizedName)) return type.SanitizedName;

        var parentNamespace = type._type.ContainingType ?? (ISymbol?)type._type.ContainingAssembly;

        string sanitizedName = type.SanitizedName;

        if (parentNamespace is not null)

            do
            {
                if (enumPropertyNameSet.Add(sanitizedName = parentNamespace.Name + sanitizedName)) return sanitizedName;

                if ((parentNamespace = parentNamespace!.ContainingType ?? (ISymbol?)parentNamespace.ContainingAssembly) is null)
                    break;

            } while (true);

        var i = 0;

        while (!enumPropertyNameSet.Add(sanitizedName + ++i)) ;

        return sanitizedName + i;
    }

    IncrementalValueProvider<ImmutableArray<T>> FindMapperAttributes<T>(
        IncrementalGeneratorInitializationContext context,
        string attrFullQualifiedName,
        Predicate<SyntaxNode> predicate,
        Func<ISymbol, SemanticModel, AttributeData, T> selector
    ) =>
        context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attrFullQualifiedName,
                (n, _) => predicate(n),
                (gasc, _) => gasc.Attributes.Select(attr => selector(gasc.TargetSymbol, gasc.SemanticModel, attr)))
            .SelectMany((i, _) => i)
            .Collect();

}
