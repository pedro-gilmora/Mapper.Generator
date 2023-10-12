#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using SourceCrafter.Mapping.Constants;
using System.Threading.Tasks;
using System.Diagnostics;
using SourceCrafter.Mapping;
using System.Collections.Generic;
using System.Collections.Immutable;

[assembly: InternalsVisibleTo("SourceCrafter.MappingGenerator.UnitTests")]
namespace SourceCrafter;

[Generator]
public class MappingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {

#if DEBUG
        Debugger.Launch();
#endif
        context.RegisterImplementationSourceOutput(
            context.CompilationProvider, 
            (_, compilation) =>
                MappingSet.objectTypeSymbol ??= compilation.GetTypeByMetadataName("System.Object")!);

        var d = FindMapperAttributes(context,
            "SourceCrafter.Mapping.Attributes.MapAttribute`1",
            static n => n is ClassDeclarationSyntax { Modifiers: { } } or InterfaceDeclarationSyntax,
            static (targetSymbol, model, attr) => (
                model,
                from: (ITypeSymbol)targetSymbol,
                to: attr.AttributeClass!.TypeArguments[0],
                ignore: (Ignore)(int)attr.ConstructorArguments[2].Value!
            ))
            .Combine(
                FindMapperAttributes(
                    context,
                    "SourceCrafter.Mapping.Attributes.MapAttribute`2",
                    static n => true,
                    static (_, model, attr) => (
                        model,
                        from: attr.AttributeClass!.TypeArguments[0],
                        to: attr.AttributeClass!.TypeArguments[1],
                        ignore: (Ignore)(int)attr.ConstructorArguments[3].Value!)));

        context.RegisterSourceOutput(d, (ctx, data) =>
        {
            MappingSet set = new();

            Action? globalBuilder = null;
            //foreach (var (model, from, to, ignore) in data.Left)
            //{
            //    AddMapper(ctx, model, from, to, ignore);
            //}
            Parallel.ForEach(
                data.Left,
                gctx => AddMapper(ctx, gctx.model, gctx.from, gctx.to, gctx.ignore));

            //foreach (var (model, from, to, ignore) in data.Right)
            //{
            //    MappingHandler.objectTypeSymbol ??= model.Compilation.GetSpecialType(SpecialType.System_Object);
            //    AddMapper(ctx, model, from, to, ignore);
            //}

            Parallel.ForEach(
                data.Right,
                gctx => AddMapper(ctx, gctx.model, gctx.from, gctx.to, gctx.ignore));

            globalBuilder?.Invoke();

            ctx.AddSource("Mappers.g", set
                .Append(@"
}")
                //.Append(set.Join(m => $"\n//{m}"))
                .ToString());

            void AddMapper(SourceProductionContext ctx, SemanticModel model, ITypeSymbol a, ITypeSymbol b, Ignore ignore)
            {
                if (set.TryGetOrAdd(model.Compilation, a, b, "from", "to", out var map, false))
                {
                    globalBuilder += () =>
                    {
                        if (map.AMapper.CanBuild)
                        {
                            Indent indent = new();

                            if (map.TypeANullable)
                                set.GenerateNullableMethod(
                                    map.AMapper,
                                    indent,
                                    "TryMap",
                                    map.TypeAFullName,
                                    map.TypeBFullName);
                            else
                                set.GenerateMethod(
                                    map.AMapper,
                                    indent,
                                    map.ToBMethodName,
                                    map.TypeAFullName,
                                    map.TypeBFullName);
                        }

                        if (map.BMapper.CanBuild)
                        {
                            Indent indent = new();

                            if (map.TypeANullable)
                                set.GenerateNullableMethod(
                                    map.BMapper,
                                    indent,
                                    "TryMap",
                                    map.TypeBFullName,
                                    map.TypeAFullName);
                            else
                                set.GenerateMethod(
                                    map.BMapper,
                                    indent,
                                    map.ToAMethodName,
                                    map.TypeBFullName,
                                    map.TypeAFullName);
                        }
                    };
                }
            }
        });

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