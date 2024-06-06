using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System;
using SourceCrafter.Bindings.Constants;
using System.Linq;
using System.Text;
using System.Diagnostics;

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
                                    attr is { AttributeClass.TypeArguments: [{ } target, { } source], ConstructorArguments: [{Value: int mapKind }, {Value: int ignore},..]  }
                                        ? new MapInfo(
                                            target,
                                            source,
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
            var type = typeSet.GetOrAdd(enumType);

            StringBuilder code = new();
            var len = code.Length;
            
            type.BuildEnumMethods(code);

            if (len == code.Length) return;
            
            code.Insert(0, @"#nullable enable
namespace SourceCrafter.EnumExtensions;

public static partial class EnumExtensions
{").Append(@"
}");
            addSource(type.ExportFullName.Replace("global::", ""), code.ToString());


        }

        foreach (var gctx in Assembly)
            if (gctx.Generate)
                set.AddMapper(gctx.From, gctx.To, gctx.Ignore, gctx.MapKind, addSource);

        foreach (var gctx in classAttributes)
            if (gctx.Generate)
                set.AddMapper(gctx.From, gctx.To, gctx.Ignore, gctx.MapKind, addSource);
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
