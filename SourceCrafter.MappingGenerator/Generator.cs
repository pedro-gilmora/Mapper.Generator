using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System;
using SourceCrafter.Bindings.Constants;
using System.Linq;
using System.Text;
using System.Diagnostics;
using SourceCrafter.MappingGenerator;
using System.Collections.Generic;
using SourceCrafter.Bindings.Helpers;
using Microsoft.CodeAnalysis.CSharp;

[assembly: InternalsVisibleTo("SourceCrafter.MappingGenerator.UnitTests")]
namespace SourceCrafter.Bindings;

[Generator]
public class Generator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        try
        {
            //context.CompilationProvider.Select(i =>
            //{
            //    var e = i.SourceModule.GetAttributes();
            //    return i;
            //});
#if DEBUG_SG
            Debugger.Launch();
#endif
            var d = FindMapperAttributes(context,
                "SourceCrafter.Bindings.Attributes.ExtendAttribute",
                static n => n is EnumDeclarationSyntax,
                static (targetSymbol, model, attr) => (ITypeSymbol)targetSymbol)
                .Combine(
                    FindMapperAttributes(context,
                        "SourceCrafter.Bindings.Attributes.BindAttribute`1",
                        static n => n is ClassDeclarationSyntax,
                        static (targetSymbol, model, attr) =>
                            attr is { AttributeClass.TypeArguments :[{ }  target], ConstructorArguments: [{Value: int mapKind }, {Value: int ignore},..] }
                                ? new MapInfo(
                                        GetTypeMapInfo((ITypeSymbol)targetSymbol),
                                        GetTypeMapInfo(target),
                                        (MappingKind)mapKind,
                                        (ApplyOn)ignore)
                                : default)
                        .Combine(
                            FindMapperAttributes(
                                context,
                                "SourceCrafter.Bindings.Attributes.BindAttribute`2",
                                static n => n is CompilationUnitSyntax,
                                static (_, model, attr) =>
                                    attr is { AttributeClass.TypeArguments: [{ } target, { } source], ConstructorArguments: [{Value: int mapKind }, {Value: int ignore},..]  }
                                        ? new MapInfo(
                                            GetTypeMapInfo(target),
                                            GetTypeMapInfo(source),
                                            (MappingKind)mapKind,
                                            (ApplyOn)ignore)
                                        : default)));

            context.RegisterSourceOutput(context.CompilationProvider.Combine(d), (ctx, info)
                =>
            {
                var (compilation, (enums, (attrs, classes))) = info;

                ProcessPosibleMappers(ctx, compilation, enums, attrs, classes);
            });

        }
        catch (Exception e)
        {
            Trace.Write("[SourceCrafter Exception]" + e.ToString());
        }
    }

    private static TypeMapInfo GetTypeMapInfo(ITypeSymbol targetSymbol)
    {
        return targetSymbol is INamedTypeSymbol { } namedSymbol && namedSymbol.ToGlobalNonGenericNamespace() == "global::SourceCrafter.Bindings.Attributes.IImplement"
            ? new(namedSymbol.TypeArguments[0], namedSymbol.TypeArguments[1])
            : new(targetSymbol, null);
    }

    private void ProcessPosibleMappers(SourceProductionContext ctx, Compilation compilation, ImmutableArray<ITypeSymbol> enums, ImmutableArray<MapInfo> OverClass, ImmutableArray<MapInfo> Assembly)
    {
        try
        {
            BuildCode(ctx.AddSource, compilation, enums, OverClass, Assembly);
        }
        catch (Exception e)
        {
            if(Debugger.IsAttached && Debugger.IsLogging())
                Debugger.Log(0, "SGExceptions", "[SouceCrafter.Bindings]: Error attempting to generate mappings and enum extensions:\n" + e.ToString());
//           code.AppendFormat(@"
///*{0}*/
//", e);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void BuildCode(Action<string, string> addSource, Compilation compilation, ImmutableArray<ITypeSymbol> enums, ImmutableArray<MapInfo> classAttributes, ImmutableArray<MapInfo> Assembly)
    {
        TypeSet typeSet = new(compilation);

        var set = new MappingSet(compilation, typeSet);

        set.Initialize(0);

        foreach (var enumType in enums)
        {
            var type = typeSet
                .GetOrAdd(GetTypeMapInfo(enumType), MappingSet.GetId(enumType));

            StringBuilder code = new(@"#nullable enable
namespace SourceCrafter.EnumExtensions;

public static partial class EnumExtensions
{");
            type.BuildEnumMethods(code);

            code.Append(@"
}");
            addSource(type.ExportFullName.Replace("global::", ""), code.ToString());


        }

        foreach (var gctx in Assembly)
            if (gctx.generate)
                set.AddMapper(gctx.from, gctx.to, gctx.ignore, gctx.mapKind, addSource);

        foreach (var gctx in classAttributes)
            if (gctx.generate)
                set.AddMapper(gctx.from, gctx.to, gctx.ignore, gctx.mapKind, addSource);
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
