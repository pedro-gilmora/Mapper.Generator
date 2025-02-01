using Microsoft.CodeAnalysis;

using SourceCrafter.Mappify.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SourceCrafter.Helpers;
using static SourceCrafter.Mappify.TypeMeta;
using System.Xml.Linq;

namespace SourceCrafter.Mappify
{
    internal sealed class TypeSet(Compilation compilation) : Set<int, TypeMeta>(t => t.Id)
    {
        internal readonly Compilation Compilation = compilation;
        internal readonly HashSet<CodeRenderer> UnsafeAccessors = new(CodeEqualityComparer.Default);
        private readonly HashSet<string> _sanitizedNames = new(StringComparer.Ordinal);

        internal ref TypeMeta GetOrAdd(ITypeSymbol membersSource, bool isDictionaryOwned = false)
        {
            ITypeSymbol? typeSymbol = null;
            
            if((membersSource = membersSource.AsNonNullable()) is INamedTypeSymbol { 
                   IsGenericType: true,
                   Name: "IImplement", 
                   ContainingNamespace.ContainingNamespace.Name: "SourceCrafter", 
                   ContainingNamespace.Name: "Mappify", 
                   TypeArguments: [{ } iFace, { } impl]
               })
            {
                membersSource = iFace;
                typeSymbol = impl;
            }
            
            var id = SymbolEqualityComparer.Default.GetHashCode(membersSource);

            ref var mapper = ref GetOrAddDefault(id, out var exists);

            if (!exists)
                // ReSharper disable once ObjectCreationAsStatement
                new TypeMeta(this, ref mapper, id, membersSource, typeSymbol, isDictionaryOwned);

            return ref mapper!;
        }

        internal string SanitizeName(ITypeSymbol type)
        {
            StringBuilder id = new();
            
            SanitizeTypeName(type);
            
            var sanitizedTypeName = id.ToString();

            if (_sanitizedNames.Add(sanitizedTypeName) || (type.ContainingType ?? (ISymbol)type.ContainingNamespace) is not { } ns)
            {
                return sanitizedTypeName;
            }

            while (ns != null && !_sanitizedNames.Add(sanitizedTypeName = ns.ToNameOnly() + sanitizedTypeName))
            {
                ns = ns.ContainingNamespace;
            }

            return sanitizedTypeName;

            void SanitizeTypeName(ITypeSymbol inType)
            {
                switch (inType)
                {
                    case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:
                        
                        id.Append("TupleOf");
                        
                        foreach (var x1 in els)
                        {
                            SanitizeTypeName(x1.Type);
                        }

                        return ;

                    case INamedTypeSymbol { IsGenericType: true, TypeArguments: { } args }:
                        
                        id.Append(inType.Name).Append("Of");
                        
                        foreach (var x1 in args)
                        {
                            SanitizeTypeName(x1);
                        }

                        return ;

                    default:
                        
                        var start = id.Length;

                        if (inType is IArrayTypeSymbol { ElementType: { } elType })
                        {
                            id.Append("ArrayOf");
                            SanitizeTypeName(elType);
                        }
                        else
                        {
                            id.Append(inType.ToTypeNameFormat());
                        }
                        
                        if(start == id.Length || char.IsUpper(id[start] )) return;
                        
                        id[start] = char.ToUpperInvariant(id[start]);
                        
                        return ;
                };
            }
        }
    }
}