global using Mapping =
    (Microsoft.CodeAnalysis.ITypeSymbol a, Microsoft.CodeAnalysis.ITypeSymbol b, SourceCrafter.Mappify.MappingKind
    mapKind, SourceCrafter.Mappify.Applyment ignore);
global using ScalarConversion = (bool exists, bool isExplicit);
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceCrafter.Mappify;

[Generator]
public class GeneratedMappers
{
    // ReSharper disable once UnusedMember.Global
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        try
        {
#if DEBUG_SG
            Debugger.Launch();
#endif
            var d = FindMapperAttributes(context,
                "SourceCrafter.Bindings.Attributes.BindAttribute`2",
                static n => n is ClassDeclarationSyntax,
                static (targetSymbol, model, attr) => attr switch
                {
                    {
                        AttributeClass.TypeArguments: [{ } target],
                        ConstructorArguments: [{ Value: int mapKind }, { Value: int ignore }, ..]
                    } => ((ITypeSymbol)targetSymbol,
                        target,
                        (MappingKind)mapKind,
                        (Applyment)ignore),
                    _ => default
                });

            context.RegisterSourceOutput(context.CompilationProvider.Combine(d), (ctx, info)
                =>
            {
                var (compilation, bindAttrs) = info;

                DiscoverMappings(compilation, bindAttrs);
            });
        }
        catch (Exception e)
        {
            Trace.Write("[SourceCrafter Exception]" + e.ToString());
        }
    }

    private static void DiscoverMappings(Compilation compilation, ImmutableArray<Mapping> bindAttrs)
    {
        Mappers mappers = new(compilation, bindAttrs);
    }

    private IncrementalValueProvider<ImmutableArray<T>> FindMapperAttributes<T>(
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

internal sealed class MemberMapper
{
    public int Id;
}