using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;
using SourceCrafter.MappingGenerator;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.XPath;

namespace SourceCrafter.Bindings;

internal sealed class TypeData
{
    internal readonly int Id;

    internal readonly string
        FullName,
        NotNullFullName,
        NonGenericFullName,
        ExportNonGenericFullName, 
        Sanitized,
        ExportFullName, 
        ExportNotNullFullName;

    private readonly Compilation _compilation;

    internal readonly ITypeSymbol _typeSymbol;

    internal CollectionInfo CollectionInfo = null!;

    internal CodeRenderer? NullableMethodUnsafeAccessor;

    public HashSet<PropertyCodeRenderer> UnsafePropertyFieldsGetters = new(new PropertyCodeEqualityComparer());

    internal readonly bool
        //IsNullable,
        AllowsNull,
        IsTupleType,
        IsReadOnly,
        IsStruct,
        IsInterface,
        IsValueType,
        IsReference,
        IsPrimitive;

    internal bool? IsIterable;

    internal bool IsRecursive;

    internal bool IsMultiMember => IsTupleType || !(IsPrimitive || IsIterable is true);

    readonly HashSet<ISymbol>? _members;

    internal readonly Func<HashSet<ISymbol>> _membersRetriever;

    IEnumerable<IFieldSymbol>? _tupleMembers;

    internal IEnumerable<ISymbol> Members =>
        _members ?? _membersRetriever();

    internal IEnumerable<IFieldSymbol> TupleElements => 
        _tupleMembers ??= ((INamedTypeSymbol)_typeSymbol).TupleElements.AsEnumerable();


    internal TypeData(Compilation compilation, TypeMapInfo typeMapInfo, int typeId)
    {
        var type = typeMapInfo.implementation ?? typeMapInfo.membersSource;

        _compilation = compilation;
        Id = typeId;

        if (type.IsNullable())
        {
            type = ((INamedTypeSymbol)type).TypeArguments[0];
            FullName = type.ToGlobalNamespace() + "?";

            var preceedingNamespace = typeMapInfo.implementation?.ContainingNamespace?.ToString() == "<global namespace>" 
                ? typeMapInfo.membersSource.ToGlobalNamespace() + "."
                : null;

            NotNullFullName = FullName = preceedingNamespace + type.ToGlobalNamespace() + "?";

            ExportFullName = (ExportNotNullFullName = typeMapInfo.membersSource?.AsNonNullable() is { } memSource
                ? memSource.ToGlobalNamespace()
                : FullName) + "?";            
        }
        else
        {
            var preceedingNamespace = typeMapInfo.implementation?.ContainingNamespace?.ToString() == "<global namespace>"
                ? typeMapInfo.membersSource.ContainingNamespace.ToGlobalNamespace() + "."
                : null;

            NotNullFullName = FullName = preceedingNamespace + type.ToGlobalNamespace();

            ExportFullName = (ExportNotNullFullName = typeMapInfo.membersSource?.AsNonNullable() is { } memSource
                ? memSource.ToGlobalNamespace()
                : FullName);
        }

        _membersRetriever = () => GetAllMembers(typeMapInfo.membersSource!); ;

        Sanitized = SanitizeTypeName(type);
        AllowsNull = type.AllowsNull();
        NonGenericFullName = type.ToGlobalNonGenericNamespace();
        ExportNonGenericFullName = type.ToGlobalNonGenericNamespace();
        IsTupleType = type.IsTupleType;
        IsValueType = type.IsValueType;
        IsStruct = type.TypeKind is TypeKind.Struct or TypeKind.Structure;
        IsPrimitive = type.IsPrimitive();  
        IsReadOnly = type.IsReadOnly;
        IsInterface = type.TypeKind == TypeKind.Interface;
        IsReference = type.IsReferenceType;

        _typeSymbol = type;
    }

    internal bool HasConversionTo(TypeData source, out bool exists, out bool isExplicit)
    {
        var sourceTS = source._typeSymbol.AsNonNullable();

        var conversion = _compilation.ClassifyConversion(_typeSymbol, sourceTS);

        exists = conversion is { Exists: true, IsReference: false } ;

        isExplicit = conversion.IsExplicit;

        if (!exists && !isExplicit)
            exists = isExplicit = _typeSymbol
                .GetMembers()
                .Any(m => m is IMethodSymbol
                    {
                        MethodKind: MethodKind.Conversion,
                        Parameters: [{ Type: { } firstParam }],
                        ReturnType: { } returnType
                    }
                    && MappingSet.AreTypeEquals(returnType, _typeSymbol)
                    && MappingSet.AreTypeEquals(firstParam, sourceTS));

        return exists;
    }

    HashSet<ISymbol> GetAllMembers(ITypeSymbol type)
    {
        HashSet<ISymbol> members = new(PropertyTypeEqualityComparer.Default);

        while (type != null)
        {
            foreach (var i in type.GetMembers())
                members.Add(i);

            type = type.BaseType!;
        }

        return members;
    }

    internal bool Equals(TypeData obj) => MappingSet._comparer.Equals(_typeSymbol, obj._typeSymbol);

    public override string ToString() => FullName;

    class PropertyCodeEqualityComparer : IEqualityComparer<PropertyCodeRenderer>
    {
        public bool Equals(PropertyCodeRenderer x, PropertyCodeRenderer y)
        {
            return x.Key == y.Key;
        }

        public int GetHashCode(PropertyCodeRenderer obj)
        {
            return obj.Key.GetHashCode();
        }
    }

    static string SanitizeTypeName(ITypeSymbol type)
    {
        switch(type)
        {
           case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:
                return "TupleOf" + string.Join("", els.Select(f => SanitizeTypeName(f.Type))) ;
           case  INamedTypeSymbol { IsGenericType: true, TypeArguments: { } args }:
                return type.Name + "Of" + string.Join("", args.Select(SanitizeTypeName));
           default:
                string typeName = type.ToTypeNameFormat();
                if (typeName.EndsWith("[]"))
                    typeName = typeName[..^2] + "Array";
                return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
        };
    }

    private class PropertyTypeEqualityComparer : IEqualityComparer<ISymbol>
    {
        internal static PropertyTypeEqualityComparer Default = new();

        public bool Equals(ISymbol x, ISymbol y)
        {
            return GetKey(x) == GetKey(y);
        }

        private string GetKey(ISymbol x)
        {
            return MappingSet._comparer.GetHashCode(x) + "|" + x.ToNameOnly();
        }

        public int GetHashCode(ISymbol obj)
        {
            return GetKey(obj).GetHashCode();
        }
    }
}

internal record struct IterableInfo(
    TypeData ItemDataType,
    EnumerableType Type,
    bool IsItemTypeNullable,
    string? AddMethod,
    string? CountProperty,
    bool Initialized);

internal record PropertyCodeRenderer(string Key, string Code) : CodeRenderer(Code);
internal record CodeRenderer(string Code)
{
    internal bool Rendered { get; set; }

    internal void Render(StringBuilder code)
    {
        if (Rendered) return;

        Rendered = true;
        code.Append(Code);
    }
}
