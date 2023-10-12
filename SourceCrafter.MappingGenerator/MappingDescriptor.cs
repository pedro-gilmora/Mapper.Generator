using System;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using SourceCrafter.Mapping.Constants;
using System.Linq;
using System.Xml.Linq;

namespace SourceCrafter.Mapping;

internal sealed class MappingDescriptor
{

    internal MappingDescriptor(ITypeSymbol typeA, ITypeSymbol typeB)
    {
        TypeA = typeA;
        TypeB = typeB;

        string
            typeAName = SanitizeMethodName(typeA),
            typeBName = SanitizeMethodName(typeB);

        ToAMethodName = GenerateMethodName(typeAName, typeBName, typeA);
        ToBMethodName = GenerateMethodName(typeBName, typeAName, typeB);
        TypeAFullName = typeA.ToGlobalizedNamespace();
        TypeBFullName = typeB.ToGlobalizedNamespace();
        TypeANullable = typeA.IsNullable();
        TypeBNullable = typeB.IsNullable();
    }

    private string SanitizeMethodName(ITypeSymbol type)
    {
        string typeName = type.ToTypeNameFormat();

        switch (type)
        {
            case INamedTypeSymbol { IsGenericType: true }:
                typeName = typeName.Replace("<", "Of").Replace(">", "_").Replace(",", "And").Replace(" ", "").TrimEnd('_', '?');
                break;
            case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:
                typeName = "TupleOf" + string.Join("And", els.Select(f => f.IsExplicitlyNamedTupleElement ? f.Name : SanitizeMethodName(f.Type)));
                break;
        }

        return typeName;
    }

    private string GenerateMethodName(string typeName, string typeName2, ITypeSymbol type)
    {
        for (
            ISymbol ns = type.ContainingNamespace; 
            typeName == typeName2 && ns != null; 
            typeName = ns.ToString() + typeName, ns = type.ContainingNamespace
        );

        if (typeName.StartsWith(typeName2))
            return "To" + typeName[typeName2.Length..];
        return "To" + typeName;
    }

    internal ITypeSymbol TypeA { get; set; }
    internal ITypeSymbol TypeB { get; set; }
    internal string TypeAFullName { get; set; }
    internal string TypeBFullName { get; set; }
    internal bool TypeANullable { get; }
    internal bool TypeBNullable { get; }
    internal Ignore IgnoreValue { get; set; }
    internal bool IsMappable { get; set; }
    internal string ToAMethodName { get; }
    internal string ToBMethodName { get; }

    internal CodeGenerator AMapper = new();
    internal CodeGenerator BMapper = new();

    internal CodeGenerator? this[ITypeSymbol type] =>
        SymbolEqualityComparer.Default.Equals(type, TypeA)
            ? AMapper
            : BMapper;

    internal bool Mapped = false;

    private readonly static StringComparer _strComparer = StringComparer.InvariantCulture;

    public int CompareTo(MappingDescriptor other)
    {
        int r = _strComparer.Compare(TypeAFullName.TrimEnd('?'), other.TypeAFullName.TrimEnd('?')),
            r1 = _strComparer.Compare(TypeBFullName.TrimEnd('?'), other.TypeBFullName.TrimEnd('?'));

        return (r, r1, _strComparer.Compare(TypeAFullName.TrimEnd('?'), other.TypeBFullName.TrimEnd('?')), _strComparer.Compare(TypeBFullName.TrimEnd('?'), other.TypeAFullName.TrimEnd('?'))) switch
        {
            (0, 0, _, _) or (_, _, 0, 0) => 0,
            _ => r != 0 ? r : r1
        };
    }
    public override string ToString() => $"'{TypeAFullName}' to = '{TypeBFullName}'";
}