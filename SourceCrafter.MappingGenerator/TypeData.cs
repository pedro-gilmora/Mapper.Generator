using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;
using SourceCrafter.Bindings;
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
    private readonly TypeSet _typeSet;
    private readonly Compilation _compilation;

    internal readonly ITypeSymbol _type;

    internal readonly CollectionInfo CollectionInfo;

    internal MemberCodeRenderer? NullableMethodUnsafeAccessor;

    public HashSet<PropertyCodeRenderer> UnsafePropertyFieldsGetters = new(PropertyCodeEqualityComparer.Default);

    internal readonly bool
        AllowsNull,
        IsTupleType,
        IsReadOnly,
        IsStruct,
        IsInterface,
        IsValueType,
        IsKeyValueType,
        IsReference,
        HasPublicZeroArgsCtor,
        IsPrimitive;

    internal bool? IsIterable;

    internal bool IsRecursive;

    internal bool IsMultiMember => IsTupleType || !(IsPrimitive || IsIterable is true);

    readonly HashSet<ISymbol>? _members;

    internal readonly Func<HashSet<ISymbol>> _membersRetriever;
    internal readonly bool DictionaryOwned;
    ImmutableArray<IFieldSymbol>? _tupleMembers;

    internal HashSet<ISymbol> Members =>
        _members ?? _membersRetriever();

    internal ImmutableArray<IFieldSymbol> TupleElements =>
        _tupleMembers ??= ((INamedTypeSymbol)_type).TupleElements;

    internal TypeData(TypeSet typeSet, Compilation compilation, TypeImplInfo typeMapInfo, int typeId, bool dictionaryOwned)
    {
        var type = typeMapInfo.Implementation ?? typeMapInfo.MembersSource;
        _typeSet = typeSet;
        _compilation = compilation;

        Id = typeId;

        DictionaryOwned = dictionaryOwned;

        if (type.IsNullable())
        {
            type = ((INamedTypeSymbol)type).TypeArguments[0];
            FullName = type.ToGlobalNamespace() + "?";

            var preceedingNamespace = typeMapInfo.Implementation?.ContainingNamespace?.ToString() == "<global namespace>"
                ? typeMapInfo.MembersSource.ToGlobalNamespace() + "."
                : null;

            NotNullFullName = FullName = preceedingNamespace + type.ToGlobalNamespace() + "?";

            ExportFullName = (ExportNotNullFullName = typeMapInfo.MembersSource?.AsNonNullable() is { } memSource
                ? memSource.ToGlobalNamespace()
                : FullName) + "?";
        }
        else
        {
            var preceedingNamespace = typeMapInfo.Implementation?.ContainingNamespace?.ToString() == "<global namespace>"
                ? typeMapInfo.MembersSource.ContainingNamespace.ToGlobalNamespace() + "."
                : null;

            NotNullFullName = FullName = preceedingNamespace + type.ToGlobalNamespace();

            ExportFullName = (ExportNotNullFullName = typeMapInfo.MembersSource?.AsNonNullable() is { } memSource
                ? memSource.ToGlobalNamespace()
                : FullName);
        }

        _membersRetriever = () => GetAllMembers(typeMapInfo.MembersSource);

        SanitizedName = SanitizeTypeName(type);
        AllowsNull = type.AllowsNull();
        NonGenericFullName = type.ToGlobalNonGenericNamespace();
        ExportNonGenericFullName = type.ToGlobalNonGenericNamespace();
        IsTupleType = type.IsTupleType;
        IsValueType = type.IsValueType;
        IsKeyValueType = NonGenericFullName.Length > 12 && NonGenericFullName[^12..] is "KeyValuePair";
        IsStruct = type.TypeKind is TypeKind.Struct or TypeKind.Structure;
        IsPrimitive = type.IsPrimitive();
        IsReadOnly = type.IsReadOnly;
        IsInterface = type.TypeKind == TypeKind.Interface;
        IsReference = type.IsReferenceType;

        HasPublicZeroArgsCtor = 
            (type is INamedTypeSymbol { InstanceConstructors: { Length: > 0 } ctors } 
             && ctors.FirstOrDefault(ctor => ctor.Parameters.IsDefaultOrEmpty)?
                    .DeclaredAccessibility is null or Accessibility.Public or Accessibility.Internal)
            || typeMapInfo.Implementation?.Kind is SymbolKind.ErrorType;

        _type = type;

        IsIterable = IsEnumerableType(NonGenericFullName, type, out CollectionInfo);
    }

    internal bool HasConversionTo(TypeData source, out bool exists, out bool isExplicit)
    {
        if (IsTupleType || source.IsTupleType) return exists = isExplicit = false;

        var sourceTS = source._type.AsNonNullable();

        var conversion = _compilation.ClassifyConversion(_type, sourceTS);

        exists = conversion is { Exists: true, IsReference: false };

        isExplicit = conversion.IsExplicit;

        if (!exists && !isExplicit)
            exists = isExplicit = _type
                .GetMembers()
                .Any(m => m is IMethodSymbol
                {
                    MethodKind: MethodKind.Conversion,
                    Parameters: [{ Type: { } firstParam }],
                    ReturnType: { } returnType
                }
                    && MappingSet.AreTypeEquals(returnType, _type)
                    && MappingSet.AreTypeEquals(firstParam, sourceTS));

        return exists;
    }

    HashSet<ISymbol> GetAllMembers(ITypeSymbol? type)
    {
        HashSet<ISymbol> members = new(PropertyTypeEqualityComparer.Default);

        while (type != null)
        {
            foreach (var i in type.GetMembers())
                members.Add(i);

            type = type.BaseType;
        }

        return members;
    }

    internal bool Equals(TypeData obj) => MappingSet._comparer.Equals(_type, obj._type);

    public override string ToString() => FullName;

    class PropertyCodeEqualityComparer : IEqualityComparer<PropertyCodeRenderer>
    {
        internal readonly static PropertyCodeEqualityComparer Default = new();
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
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                    typeName = SanitizeTypeName(elType) + "Array";
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

            if (!definedByInt.Contains(distinctIntCase))
            {
                definedByInt += caseComma + distinctIntCase + ":";
            }

            definedByName += caseComma + "case nameof(" + fullMemberName + "):";

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

    public static global::System.Collections.Immutable.ImmutableArray<string> GetDescriptions<T>() where T : global::SourceCrafter.Bindings.Helpers.IEnum<{0}> 
        => _cached{1}Descriptions ??= global::System.Collections.Immutable.ImmutableArray.Create(
            {3});

    public static global::System.Collections.Immutable.ImmutableArray<string> GetNames<T>() where T : global::SourceCrafter.Bindings.Helpers.IEnum<{0}>
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
                return true; 
            default: 
                return false; 
        }}
    }}

    public static bool IsDefined<T>(this int value) where T : global::SourceCrafter.Bindings.Helpers.IEnum<{0}>
    {{
        switch(value)
        {{
            {8}
                return true;
            default: 
                return false; 
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

    internal void BuildKeyValuePair(StringBuilder sb, string _params)
    {
        sb.AppendFormat("new {0}({1})", NonGenericFullName, _params);
    }

    internal void BuildTuple(StringBuilder sb, string _params)
    {
        sb.AppendFormat("({0})", _params);
    }

    internal void BuildType(StringBuilder sb, string props)
    {
        sb.AppendFormat(@"new {0}
        {{{1}
        }}", NotNullFullName, props);
    }

    private string GetEnumDescription(IFieldSymbol m) => $@"""{m
        .GetAttributes()
        .FirstOrDefault(a => a.AttributeClass?.ToGlobalNamespace() is "global::System.ComponentModel.DescriptionAttribute")
        ?.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? m.Name.Wordify()}""";

    private class PropertyTypeEqualityComparer : IEqualityComparer<ISymbol>
    {
        internal readonly static PropertyTypeEqualityComparer Default = new();

        public bool Equals(ISymbol x, ISymbol y) => GetKey(x) == GetKey(y);

        private string GetKey(ISymbol x) => MappingSet._comparer.GetHashCode(x) + "|" + x.ToNameOnly();

        public int GetHashCode(ISymbol obj) => GetKey(obj).GetHashCode();
    }

    bool IsEnumerableType(string nonGenericFullName, ITypeSymbol type, out CollectionInfo info)
    {
        if (type.IsPrimitive(true))
        {
            info = default!;
            return false;
        }

        switch (nonGenericFullName)
        {
            case "global::System.Collections.Generic.Dictionary" or "global::System.Collections.Generic.IDictionary"
            :
                info = GetCollectionInfo(EnumerableType.Dictionary, GetEnumerableType(type, true));

                return true;

            case "global::System.Collections.Generic.Stack"
            :
                info = GetCollectionInfo(EnumerableType.Stack, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                info = GetCollectionInfo(EnumerableType.Queue, GetEnumerableType(type));

                return true;

            case "global::System.ReadOnlySpan"
            :

                info = GetCollectionInfo(EnumerableType.ReadOnlySpan, GetEnumerableType(type));

                return true;

            case "global::System.Span"
            :
                info = GetCollectionInfo(EnumerableType.Span, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                info = GetCollectionInfo(EnumerableType.Collection, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                info = GetCollectionInfo(EnumerableType.ReadOnlyCollection, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                info = GetCollectionInfo(EnumerableType.Enumerable, GetEnumerableType(type));

                return true;

            default:
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                {
                    info = GetCollectionInfo(EnumerableType.Array, elType);

                    return true;
                }
                else
                    foreach (var item in type.AllInterfaces)
                        if (IsEnumerableType(item.ToGlobalNonGenericNamespace(), item, out info))
                            return true;
                break;
        }

        info = default!;

        return false;
    }

    ITypeSymbol GetEnumerableType(ITypeSymbol enumerableType, bool isDictionary = false)
    {
        if (isDictionary)
            return ((INamedTypeSymbol)enumerableType)
                .AllInterfaces
                .First(i => i.Name.StartsWith("IEnumerable"))
                .TypeArguments
                .First();

        return ((INamedTypeSymbol)enumerableType)
            .TypeArguments
            .First();
    }

    private CollectionInfo GetCollectionInfo(EnumerableType enumerableType, ITypeSymbol typeSymbol)
    {
        typeSymbol = typeSymbol.AsNonNullable();

        TypeData itemDataType = _typeSet.GetOrAdd(typeSymbol, enumerableType == EnumerableType.Dictionary);

        return enumerableType switch
        {
#pragma warning disable format
            EnumerableType.Dictionary =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), true, true,  true,   false,  "Add",      "Count"),
            EnumerableType.Queue =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), false, true, true,   false,  "Enqueue",  "Count"),
            EnumerableType.Stack =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), false, true, true,   false,  "Push",     "Count"),
            EnumerableType.Enumerable =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), false, true, false,  true,   null,       "Length"),
            EnumerableType.ReadOnlyCollection =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), true, true,  true,   false,  "Add",      "Count"),
            EnumerableType.ReadOnlySpan =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), true, true,  true,   true,   null,       "Length"),
            EnumerableType.Collection =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), true, false, true,   false,  "Add",      "Count"),
            EnumerableType.Span =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), true, false, true,   true,   null,       "Length"),
            _ =>
                new(itemDataType, enumerableType, typeSymbol.IsNullable(), true, false, true,   true,   null,       "Length")
#pragma warning restore format
        };
    }
}

internal record PropertyCodeRenderer(string Key, string Code) : MemberCodeRenderer(Code);

internal record MemberCodeRenderer(string Code)
{
    internal bool Rendered { get; set; }

    internal void Render(StringBuilder code)
    {
        if (Rendered) return;

        Rendered = true;
        code.Append(Code);
    }
}
