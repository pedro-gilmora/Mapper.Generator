using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

[Generator]
public class MappingGenerator : ISourceGenerator
{
    private const string AttributeFullName = "SourceCrafter.Attributes.MapAttribute`2";

    public void Initialize(GeneratorInitializationContext context)
    {
        // No initialization required
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // Retrieve the compilation for which the generator is executing
        Compilation compilation = context.Compilation;

        // Retrieve the INamedTypeSymbol for the MapAttribute<TTarget, TSource> attribute
        INamedTypeSymbol attributeSymbol = !;

        if (compilation.GetTypeByMetadataName(AttributeFullName) == null)
        {
            // Attribute not found, exit the generator
            return;
        }

        // Retrieve the attributed types from the compilation
        IEnumerable<INamedTypeSymbol> attributedTypes = GetAttributedTypes(compilation, attributeSymbol);

        // Generate the mapping code for each attributed type
        foreach (INamedTypeSymbol attributedType in attributedTypes)
        {
            string mappingCode = GenerateMappingCode(attributedType);
            string mappingFileName = $"{attributedType.Name}Mapping.cs";
            SourceText sourceText = SourceText.From(mappingCode, System.Text.Encoding.UTF8);
            context.AddSource(mappingFileName, sourceText);
        }
    }

    private IEnumerable<INamedTypeSymbol> GetAttributedTypes(Compilation compilation, INamedTypeSymbol attributeSymbol)
    {
        // Retrieve all types from the compilation
        IEnumerable<INamedTypeSymbol> allTypes = compilation.GetSymbolsWithName(symbolName => true, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>();

        // Retrieve the attributed types
        IEnumerable<INamedTypeSymbol> attributedTypes = allTypes
            .Where(typeSymbol => typeSymbol.GetAttributes().Any(attribute => true == attribute.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default)));

        return attributedTypes;
    }

    private string GenerateMappingCode(INamedTypeSymbol attributedType)
    {
        string sourceTypeName = attributedType.TypeArguments[1].ToDisplayString();
        string targetTypeName = attributedType.TypeArguments[0].ToDisplayString();

        return $@"
using System;

namespace Namespace
{{
    public static class {attributedType.Name}Mapping
    {{
        public static {targetTypeName} Map({sourceTypeName} source)
        {{
            // Mapping implementation here
            throw new NotImplementedException();
        }}
    }}
}}
";
    }
}
