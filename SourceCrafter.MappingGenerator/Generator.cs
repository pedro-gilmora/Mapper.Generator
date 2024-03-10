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
                "SourceCrafter.Bindings.Attributes.BindAttribute`1",
                static n => n is ClassDeclarationSyntax { },
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
                                : default));

            context.RegisterSourceOutput(context.CompilationProvider.Combine(d), (ctx, info)
                => ProcessPosibleMappers(ctx, info.Left, info.Right.Left, info.Right.Right));

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

    private void ProcessPosibleMappers(SourceProductionContext ctx, Compilation compilation, ImmutableArray<MapInfo> OverClass, ImmutableArray<MapInfo> Assembly)
    {
        StringBuilder code = new(@"#nullable enable
namespace SourceCrafter.Mappings;

public static partial class MappingsExtensions
{");
        try
        {
            BuildCode(code, compilation, OverClass, Assembly);
        }
        catch (Exception e)
        {
            code.AppendFormat(@"
/*{0}*/
", e);
        }

        ctx.AddSource("Mappers.g", code
            .Append("}")
            .ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void BuildCode(StringBuilder code, Compilation compilation, ImmutableArray<MapInfo> classAttributes, ImmutableArray<MapInfo> Assembly)
    {
        var set = new MappingSet(compilation, code);

        set.Initialize(0);

        Action? buildAll = null;

        foreach (var gctx in Assembly)
            if(gctx.generate)
                buildAll += set.AddMapper(gctx.from, gctx.to, gctx.ignore, gctx.mapKind);

        foreach (var gctx in classAttributes)
            if (gctx.generate)
                buildAll += set.AddMapper(gctx.from, gctx.to, gctx.ignore, gctx.mapKind);

        buildAll?.Invoke();
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
