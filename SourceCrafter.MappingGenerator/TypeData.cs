using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;
using SourceCrafter.MappingGenerator;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;

namespace SourceCrafter.Bindings;

internal sealed class TypeData
{
    internal readonly int Id;

    internal readonly string
        FullName,
        NotNullFullName,
        NonGenericFullName,
        ExportNonGenericFullName,
        SanitizedName,
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

        SanitizedName = SanitizeTypeName(type);
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

        exists = conversion is { Exists: true, IsReference: false };

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
        switch (type)
        {
            case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:
                return "TupleOf" + string.Join("", els.Select(f => SanitizeTypeName(f.Type)));
            case INamedTypeSymbol { IsGenericType: true, TypeArguments: { } args }:
                return type.Name + "Of" + string.Join("", args.Select(SanitizeTypeName));
            default:
                string typeName = type.ToTypeNameFormat();
                if (typeName.EndsWith("[]"))
                    typeName = typeName[..^2] + "Array";
                return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
        };
    }

    internal void BuildEnumMethods(StringBuilder code)
    {
        var members = Members.OfType<IFieldSymbol>().ToImmutableArray();

        if (members.Length == 0) return;

        string?
            collectionsComma = null,
            caseComma = null,
            values = null,
            descriptions = null,
            names = null,
            name = null,
            description = null,
            definedByName = null,
            definedByInt = "",
            tryGetValue = null,
            tryGetName = null,
            tryGetDesc = null;

        foreach (var m in members)
        {
            string fullMemberName = MemberFullName(m);

            values += collectionsComma + fullMemberName;

            string descriptionStr = GetEnumDescription(m);

            descriptions += collectionsComma + descriptionStr;
           
            names += collectionsComma + "nameof(" + fullMemberName + ")";
            
            name += caseComma + "case " + fullMemberName + ": return nameof(" + fullMemberName + ");";
            
            description += caseComma + "case " + fullMemberName + @": 
                return " + descriptionStr + ";";
            
            string distinctIntCase = "case " + Convert.ToString(m.ConstantValue!);

            if(!definedByInt.Contains(distinctIntCase))
            {
                definedByInt += caseComma + distinctIntCase  + @": 
                return true;";
            }

            definedByName += caseComma + "case nameof(" + fullMemberName + @"): 
                return true;";
            
            tryGetValue += caseComma + "case nameof(" + fullMemberName + @"): 
                result = " + fullMemberName + @"; 
                return true;";
            
            tryGetName += caseComma + "case " + fullMemberName + @": 
                result = nameof(" + fullMemberName + @"); 
                return true;";
            
            tryGetDesc += caseComma + "case " + fullMemberName + @": 
                result = " + descriptionStr + @"; 
                return true;";

            collectionsComma ??= "," + (caseComma ??= @"
            ");
        }

        code.AppendFormat(@"
    private static global::System.Collections.Immutable.ImmutableArray<{0}>? _cached{1}Values;
    
    public static global::System.Collections.Immutable.ImmutableArray<{0}>? Get{1}Values() 
        => _cached{1}Values ??= global::System.Collections.Immutable.ImmutableArray.Create(
            {2});
    
    private static global::System.Collections.Immutable.ImmutableArray<string>?
        _cached{1}Descriptions,
        _cached{1}Names;

    public static global::System.Collections.Immutable.ImmutableArray<string> Get{1}Descriptions() 
        => _cached{1}Descriptions ??= global::System.Collections.Immutable.ImmutableArray.Create(
            {3});

    public static global::System.Collections.Immutable.ImmutableArray<string> Get{1}Names() 
        => _cached{1}Names ??= global::System.Collections.Immutable.ImmutableArray.Create(
            {4});

    public static string? GetName(this {0} value, bool throwOnNotFound = false) 
	{{
		switch(value)
        {{
            {5}
            default: return throwOnNotFound ? value.ToString() : throw new Exception(""The value is not a valid identifier for type [{0}]""); 
        }}
    }}

    public static string GetDescription(this {0} value, bool throwOnNotFound = false) 
    {{
		switch(value)
        {{
            {6}
            default: return throwOnNotFound ? value.ToString() : throw new Exception(""The value has no description""); 
        }}
    }}

    public static bool IsDefined<T>(this string value) where T : global::SourceCrafter.Bindings.Helpers.IEnum<{0}>
    {{
		switch(value)
        {{
            {7}
            default: return false; 
        }}
    }}

    public static bool IsDefined<T>(this int value) where T : global::SourceCrafter.Bindings.Helpers.IEnum<{0}>
    {{
        switch(value)
        {{
            {8}
            default: return false; 
        }}
    }}

    public static bool TryGetValue(this string value, out {0} result)
    {{
        switch(value)
        {{
            {9}
            default: result = default; return false; 
        }}
    }}

    public static bool TryGetName(this {0} value, out string result)
    {{
        switch(value)
        {{
            {10}
            default: result = default!; return false; 
        }}
    }}

    public static bool TryGetDescription(this {0} value, out string result)
    {{
        switch(value)
        {{
            {11}
            default: result = default!; return false; 
        }}
    }}",
                NotNullFullName,
                SanitizedName,
                values,
                descriptions,
                names,
                name,
                description,
                definedByName,
                definedByInt,
                tryGetValue,
                tryGetName,
                tryGetDesc);

        string MemberFullName(IFieldSymbol m) => NotNullFullName + "." + m.Name;
    }

    private string GetEnumDescription(IFieldSymbol m)
    {
        return "\"" + (m
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToGlobalNamespace() == "global::System.ComponentModel.DescriptionAttribute")
            ?.ConstructorArguments[0].Value?.ToString() ?? m.Name.Wordify()) + "\"";
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
