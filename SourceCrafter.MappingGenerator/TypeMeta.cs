﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace SourceCrafter.Bindings;

internal sealed class TypeMeta
{
    internal readonly int Id;

    internal readonly string
        FullName,
        NotNullFullName;

    private readonly string         _nonGenericFullName;

    internal readonly string
        ExportNonGenericFullName,
        SanitizedName,
        ExportFullName,
        ExportNotNullFullName;

    private readonly TypeSet _typeSet;
    private readonly Compilation _compilation;

    internal readonly ITypeSymbol Type;

    internal readonly CollectionInfo CollectionInfo;

    internal readonly MemberCodeRenderer? NullableMethodUnsafeAccessor = null;

    public readonly HashSet<PropertyCodeRenderer> UnsafePropertyFieldsGetters = new(PropertyCodeEqualityComparer.Default);

    internal readonly bool
        AllowsNull,
        IsTupleType,
        IsObject,
        IsReadOnly,
        IsStruct,
        IsInterface,
        IsValueType,
        IsKeyValueType,
        IsReference,
        HasReachableZeroArgsCtor,
        IsPrimitive,
        IsEnum,
        IsIterable;

    internal bool IsRecursive;
    internal readonly bool DictionaryOwned;

    private MemberMeta[]? _members = null;

    private readonly Func<MemberMeta[]> _getMembers = null!;

    internal bool HasMembers => !Members.IsEmpty;

    internal Span<MemberMeta> Members => _members ??= _getMembers();

    internal TypeMeta(TypeSet typeSet, Compilation compilation, TypeImplInfo typeMapInfo, int typeId, bool dictionaryOwned)
    {
        var type = typeMapInfo.Implementation ?? typeMapInfo.MembersSource;
        _typeSet = typeSet;
        _compilation = compilation;

        Id = typeId;

        DictionaryOwned = dictionaryOwned;

        type.TryGetNullable(out type, out var isNullable);

        var precedingNamespace = typeMapInfo.Implementation?.ContainingNamespace?.ToString() == "<global namespace>"
            ? typeMapInfo.MembersSource.ContainingNamespace.ToGlobalNamespaced() + "."
            : null;

        NotNullFullName = precedingNamespace + type.ToGlobalNamespaced();
        FullName = NotNullFullName;

        ExportFullName = ExportNotNullFullName = typeMapInfo.MembersSource.AsNonNullable() is { } memSource
            ? memSource.ToGlobalNamespaced().TrimEnd('?')
            : FullName;

        if (isNullable)
        {
            FullName += "?";
            ExportFullName += "?";
        }

        SanitizedName = SanitizeTypeName(type);
        AllowsNull = type.AllowsNull();
        _nonGenericFullName = type.ToGlobalNonGenericNamespace();
        IsObject = _nonGenericFullName == "object";
        ExportNonGenericFullName = type.ToGlobalNonGenericNamespace();
        IsTupleType = type.IsTupleType;
        IsValueType = type.IsValueType;
        IsKeyValueType = _nonGenericFullName.Length > 12 && _nonGenericFullName[^12..] is "KeyValuePair";
        IsStruct = type.TypeKind is TypeKind.Struct;
        IsPrimitive = type.IsPrimitive();
        IsReadOnly = type.IsReadOnly;
        IsInterface = type.TypeKind == TypeKind.Interface;
        IsReference = type.IsReferenceType;

        HasReachableZeroArgsCtor =
            (type is INamedTypeSymbol { InstanceConstructors: { Length: > 0 } ctors }
             && ctors.FirstOrDefault(ctor => ctor.Parameters.IsDefaultOrEmpty)?
                    .DeclaredAccessibility is null or Accessibility.Public or Accessibility.Internal)
            || typeMapInfo.Implementation?.Kind is SymbolKind.ErrorType;

        Type = type;

        IsIterable = IsEnumerableType(_nonGenericFullName, type, out CollectionInfo);

        _getMembers = IsTupleType
            ? () => GetAllMembers(((INamedTypeSymbol)typeMapInfo.MembersSource!.AsNonNullable()).TupleElements)
            : () => GetAllMembers(typeMapInfo.MembersSource!.AsNonNullable());
    }

    internal bool HasConversionTo(TypeMeta target, out ScalarConversion toTarget, out ScalarConversion toSource)
    {
        var hasConversionResult = hasConversion(this, target, out toTarget);

        if (target.Id == Id) {
            toSource = toTarget;
            return hasConversionResult;
        }

        return hasConversionResult | hasConversion(target, this, out toSource);

        bool hasConversion(TypeMeta source, TypeMeta target, out ScalarConversion info)
        {
            if ((source, target) is not (
                ({ IsTupleType: false }, { IsTupleType: false }) and
                ({ DictionaryOwned: false, IsKeyValueType: false }, { DictionaryOwned: false, IsKeyValueType: false })
            ))
            {
                info = default;
                return false;
            }

            ITypeSymbol 
                targetTypeSymbol = target.Type.AsNonNullable(),
                sourceTypeSymbol = source.Type;

            var conversion = _compilation.ClassifyConversion(sourceTypeSymbol, targetTypeSymbol);

            info = (conversion.Exists && (source.IsValueType || source.FullName == "string" || source.IsObject), conversion.IsExplicit);

            if (!info.exists)
            {
                if (!info.isExplicit)
                {
                    info.exists = info.isExplicit = sourceTypeSymbol
                        .GetMembers()
                        .Any(m => m is IMethodSymbol
                            {
                                MethodKind: MethodKind.Conversion,
                                Parameters: [{ Type: { } firstParam }],
                                ReturnType: { } returnType
                            }
                            && MappingSet.AreTypeEquals(returnType, sourceTypeSymbol)
                            && MappingSet.AreTypeEquals(firstParam, targetTypeSymbol)
                        );
                }
                else if (source.IsInterface && sourceTypeSymbol.AllInterfaces.FirstOrDefault(target.Equals) is { } impl)
                {
                    info.exists = !(info.isExplicit = false);
                }
            }

            info.exists |= info.isExplicit |= source.IsObject && !target.IsObject;

            return info.exists;
        }
    }


    private MemberMeta[] GetAllMembers(ITypeSymbol type)
    {
        HashSet<MemberMeta> members = new(PropertyNameEqualityComparer.Default);

        HashSet<int> ids = [];

        var isInterface = type.TypeKind == TypeKind.Interface;

        GetMembers(type);

        return [.. members.OrderBy(m => m.Position)];

        void GetMembers(ITypeSymbol type, bool isFirstLevel = true)
        {
            if (type?.Name is not (null or "Object"))
            {
                var i = 0;
                foreach (var member in type!.GetMembers())
                {
                    if (member is IFieldSymbol { AssociatedSymbol: IPropertySymbol s })
                    {
                        ids.Add(MappingSet.Comparer.GetHashCode(s));
                        continue;
                    }

                    if (member.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Friend or Accessibility.Public))
                        continue;

                    var isAccessible = member.DeclaredAccessibility is Accessibility.Public ||
                            MappingSet.Comparer.Equals(_compilation.SourceModule, member.ContainingModule);


                    switch (member)
                    {
                        case IPropertySymbol
                        {
                            ContainingType.Name: not ['I', 'E', 'n', 'u', 'm', 'e', 'r', 'a', 't', 'o', 'r', ..],
                            IsIndexer: false,
                            Type: { } memberType,
                            RefCustomModifiers: { },
                            IsImplicitlyDeclared: var impl,
                            IsStatic: false,
                            IsReadOnly: var isReadonly,
                            IsWriteOnly: var isWriteOnly,
                            SetMethod: var setMethod
                        } when isInterface || !impl:
                            var id = MappingSet.Comparer.GetHashCode(member);

                            members.Add(
                                new(id,
                                    member.ToNameOnly(),
                                    memberType.IsNullable(),
                                    isReadonly,
                                    isWriteOnly,
                                    setMethod?.IsInitOnly == true,
                                    true,
                                    true,
                                    member.GetAttributes())
                                {
                                    Type = _typeSet.GetOrAdd(memberType),
                                    CanBeInitialized = !isReadonly,
                                    IsAutoProperty = ids.Contains(id),
                                    Position = i++,
                                    IsAccessible = isAccessible
                                });

                            continue;

                        case IFieldSymbol
                        {
                            ContainingType.Name: not ['I', 'E', 'n', 'u', 'm', 'e', 'r', 'a', 't', 'o', 'r', ..],
                            Type: { } memberType,
                            IsStatic: false,
                            AssociatedSymbol: var associated,
                            IsReadOnly: var isReadonly,

                        }:
                            members.Add(
                                new(MappingSet.Comparer.GetHashCode(member),
                                    member.ToNameOnly(),
                                    memberType.IsNullable(),
                                    true,
                                    !isReadonly,
                                    true,
                                    Attributes: member.GetAttributes())
                                {
                                    Type = _typeSet.GetOrAdd(memberType.AsNonNullable()),
                                    Position = i++,
                                    CanBeInitialized = !isReadonly,
                                    IsAccessible = isAccessible
                                });

                            continue;

                        default:
                            continue;
                    }
                }

                if(type.BaseType != null)
                    GetMembers(type.BaseType);

                if (!isFirstLevel) return;
            
                foreach (var iface in type.AllInterfaces)
                    GetMembers(iface, false);
            }
        }
    }

    private MemberMeta[] GetAllMembers(ImmutableArray<IFieldSymbol> tupleFields)
    {
        HashSet<MemberMeta> members = new(PropertyNameEqualityComparer.Default);

        if (tupleFields.IsDefaultOrEmpty) return [];

        foreach (var member in tupleFields)
            members.Add(
                new(MappingSet.Comparer.GetHashCode(member),
                    member.ToNameOnly(),
                    member.Type.IsNullable(),
                    false,
                    false,
                    true,
                    Attributes: ImmutableArray<AttributeData>.Empty)
                {
                    Type = _typeSet.GetOrAdd(member.Type.AsNonNullable())
                });

        return [.. members];
    }

    internal bool Equals(TypeMeta obj) => MappingSet.Comparer.Equals(Type, obj.Type);

    public override string ToString() => FullName;

    private class PropertyCodeEqualityComparer : IEqualityComparer<PropertyCodeRenderer>
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

    private string SanitizeTypeName(ITypeSymbol type)
    {
        //string sanitizedTypeName = sanitizeTypeName();

        //if (!_typeSet.sanitizedNames.Add(sanitizedTypeName) && type.ContainingNamespace is { } ns)
        //{
        //    while (ns != null && !_typeSet.sanitizedNames.Add(sanitizedTypeName = ns.ToNameOnly() + sanitizedTypeName))
        //    {
        //        ns = ns.ContainingNamespace;
        //    }
        //}

        //return sanitizedTypeName;

        //string sanitizeTypeName()
        //{
        switch (type)
        {
            case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

                return "TupleOf" + string.Join("", els.Select(f => SanitizeTypeName(f.Type)));
                
            case INamedTypeSymbol { IsGenericType: true, TypeArguments: { } args }:
                
                return type.Name + "Of" + string.Join("", args.Select(SanitizeTypeName));
                
            default:
                
                var typeName = type.ToTypeNameFormat();
                    
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                    typeName = SanitizeTypeName(elType) + "Array";
                    
                return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
        };
        //}
    }

    internal void BuildEnumMethods(StringBuilder code)
    {
        var members = Type.GetMembers();

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

        foreach (var m in members.OfType<IFieldSymbol>())
        {
            var fullMemberName = MemberFullName(m);

            values += collectionsComma + fullMemberName;

            var descriptionStr = GetEnumDescription(m);

            descriptions += collectionsComma + descriptionStr;

            names += collectionsComma + "nameof(" + fullMemberName + ")";

            name += caseComma + "case " + fullMemberName + ": return nameof(" + fullMemberName + ");";

            description += caseComma + "case " + fullMemberName + @": 
                return " + descriptionStr + ";";

            var distinctIntCase = "case " + Convert.ToString(m.ConstantValue!);

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

        code.Append(@"
        
    private static ")
            .Append(NotNullFullName)
            .Append("[]? _cached")
            .Append(SanitizedName)
            .Append(@"Values;
    
    public static global::System.Span<")
            .Append(NotNullFullName)
            .Append("> GetValues(this ")
            .Append(NotNullFullName)
            .Append(@" _)
    {
        if(_cached")
            .Append(SanitizedName)
            .Append("Values is not null) return _cached")
            .Append(SanitizedName)
            .Append(@"Values;

        lock(__lock) return _cached")
            .Append(SanitizedName)
            .Append("Values ??= new ")
            .Append(NotNullFullName)
            .Append(@" [] {
            ")
            .Append(values)
            .Append(@"
        };
    }
    
    private static string[]?
        _cached")
            .Append(SanitizedName)
            .Append(@"Descriptions,
        _cached")
            .Append(SanitizedName)
            .Append(@"Names;

    public static global::System.Span<string> GetDescriptions(this ")
            .Append(NotNullFullName)
            .Append(@" _)
    {
        if(_cached")
            .Append(SanitizedName)
            .Append("Descriptions is not null) return _cached")
            .Append(SanitizedName)
            .Append(@"Descriptions;

        lock(__lock) return _cached")
            .Append(SanitizedName)
            .Append(@"Descriptions ??= new string [] {
            ")
            .Append(descriptions)
            .Append(@"
        };
    }

    public static global::System.Span<string> GetNames(this ")
            .Append(NotNullFullName)
            .Append(@" _)
    {
        if(_cached")
            .Append(SanitizedName)
            .Append("Names is not null) return _cached")
            .Append(SanitizedName)
            .Append(@"Names;

        lock(__lock) return _cached")
            .Append(SanitizedName)
            .Append(@"Names ??= new string [] {
            ")
            .Append(names)
            .Append(@"
        };
    }

    public static string? GetName(this ")
            .Append(NotNullFullName)
            .Append(@" value, bool throwOnNotFound = false) 
	{
		switch(value)
        {
            ")
            .Append(name)
            .Append(@"
            default: return throwOnNotFound ? value.ToString() : throw new global::System.Exception(""The value is not a valid identifier for type [")
            .Append(NotNullFullName)
            .Append(@"]""); 
        }
    }

    public static string GetDescription(this ")
            .Append(NotNullFullName)
            .Append(@" value, bool throwOnNotFound = false) 
    {
		switch(value)
        {
            ")
            .Append(description)
            .Append(@"
            default: return throwOnNotFound ? value.ToString() : throw new global::System.Exception(""The value has no description""); 
        }
    }

    public static bool IsDefined(this ")
            .Append(NotNullFullName)
            .Append(@" _, string value)
    {
		switch(value)
        {
            ")
            .Append(definedByName)
            .Append(@"
                return true; 
            default: 
                return false; 
        }
    }

    public static bool IsDefined(this ")
            .Append(NotNullFullName)
            .Append(@" _, int value)
    {
        switch(value)
        {
            ")
            .Append(definedByInt)
            .Append(@"
                return true;
            default: 
                return false; 
        }
    }

    public static bool TryGetValue(this string value, out ")
            .Append(NotNullFullName)
            .Append(@" result)
    {
        switch(value)
        {
            ")
            .Append(tryGetValue)
            .Append(@"
            default: result = default; return false; 
        }
    }

    public static bool TryGetName(this ")
            .Append(NotNullFullName)
            .Append(@" value, out string result)
    {
        switch(value)
        {
            ")
            .Append(tryGetName)
            .Append(@"
            default: result = default!; return false; 
        }
    }

    public static bool TryGetDescription(this ")
            .Append(NotNullFullName)
            .Append(@" value, out string result)
    {
        switch(value)
        {
            ")
            .Append(tryGetDesc)
            .Append(@"
            default: result = default!; return false; 
        }
    }");

        string MemberFullName(IFieldSymbol m) => NotNullFullName + "." + m.Name;
    }

    internal void BuildKeyValuePair(StringBuilder sb, string _params)
    {
        sb.AppendFormat("new {0}({1})", _nonGenericFullName, _params);
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
        .FirstOrDefault(a => a.AttributeClass?.ToGlobalNamespaced() is "global::System.ComponentModel.DescriptionAttribute")
        ?.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? m.Name.Wordify()}""";

    private class PropertyNameEqualityComparer : IEqualityComparer<MemberMeta>
    {
        internal readonly static PropertyNameEqualityComparer Default = new();

        public bool Equals(MemberMeta x, MemberMeta y) => x.Name == y.Name;

        public int GetHashCode(MemberMeta obj) => obj.Name.GetHashCode();
    }

    private bool IsEnumerableType(string nonGenericFullName, ITypeSymbol type, out CollectionInfo info)
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

    private static ITypeSymbol GetEnumerableType(ITypeSymbol enumerableType, bool isDictionary = false)
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
        var itemDataType = _typeSet.GetOrAdd((typeSymbol = typeSymbol.AsNonNullable()), enumerableType == EnumerableType.Dictionary);

        return enumerableType switch
        {
#pragma warning disable format
            EnumerableType.Dictionary =>
                new(itemDataType, 
                    enumerableType, 
                    typeSymbol.IsNullable(), 
                    true, 
                    true, 
                    true, 
                    false, 
                    "Add", 
                    "Count"),
            EnumerableType.Queue =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    true,
                    true,
                    false,
                    "Enqueue",
                    "Count"),
            EnumerableType.Stack =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    true,
                    true,
                    false,
                    "Push",
                    "Count"),
            EnumerableType.Enumerable =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    true,
                    false,
                    true,
                    null,
                    "Length"),
            EnumerableType.ReadOnlyCollection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.ReadOnlySpan =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    true,
                    null,
                    "Length"),
            EnumerableType.Collection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    false,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.Span =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    false,
                    true,
                    true,
                    null,
                    "Length"),
            _ =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    false,
                    true,
                    true,
                    null,
                    "Length")
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