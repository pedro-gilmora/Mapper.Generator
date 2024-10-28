using Microsoft.CodeAnalysis;

using SourceCrafter.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceCrafter.Mappify
{
    internal sealed class TypeSet(Compilation compilation) : Set<int, TypeMeta>(t => t.Id)
    {
        internal readonly Compilation Compilation = compilation;

        private readonly HashSet<string> _sanitizedNames = new(StringComparer.Ordinal);

        internal TypeMeta GetOrAdd(ITypeSymbol typeSymbol, ITypeSymbol? membersSource = null, bool isDictionaryOwned = false)
        {
            var id = SymbolEqualityComparer.Default.GetHashCode(typeSymbol);

            ref var mapper = ref base.GetOrAddDefault(typeSymbol, out var exists);

            if (exists) return mapper;

            return mapper = new(this, id, typeSymbol, membersSource, isDictionaryOwned);
        }

        internal string SanitizeName(ITypeSymbol type)
        {
            string sanitizedTypeName = sanitizeTypeName(type);

            if (!_sanitizedNames.Add(sanitizedTypeName) && type.ContainingNamespace is { } ns)
            {
                while (ns != null && !_sanitizedNames.Add(sanitizedTypeName = ns.ToNameOnly() + sanitizedTypeName))
                {
                    ns = ns.ContainingNamespace;
                }
            }

            return sanitizedTypeName;

            static string sanitizeTypeName(ITypeSymbol type)
            {
                switch (type)
                {
                    case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

                        return "TupleOf" + string.Join("", els.Select(f => sanitizeTypeName(f.Type)));

                    case INamedTypeSymbol { IsGenericType: true, TypeArguments: { } args }:

                        return type.Name + "Of" + string.Join("", args.Select(sanitizeTypeName));

                    default:

                        var typeName = type.ToTypeNameFormat();

                        if (type is IArrayTypeSymbol { ElementType: { } elType }) typeName = sanitizeTypeName(elType) + "Array";

                        return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
                };
            }
        }
    }
}