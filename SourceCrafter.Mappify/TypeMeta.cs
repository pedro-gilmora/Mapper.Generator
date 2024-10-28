using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceCrafter.Helpers;

namespace SourceCrafter.Mappify;

internal sealed class TypeMeta
{
    internal readonly int Id;
    internal readonly TypeSet Types;
    internal readonly ITypeSymbol Symbol;
    internal readonly Set<MemberMeta> Members;

    internal readonly string
        Name,
        ShortName,
        FullName,
        FullNonGenericName,
        ExportFullName,
        ExportNonFullGenericName;

    internal readonly bool
        IsTupleType,
        DictionaryOwned,
        IsKeyValueType,
        IsValueType,
        IsCollection,
        IsInterface,
        IsObject,
        HasZeroArgsCtor,
        IsMemberless;

    internal bool IsRecursive;
    internal CollectionMeta Collection = default;
    internal string SanitizedName;
    internal bool IsPrimitive;

    internal TypeMeta(
        TypeSet types,
        int typeAId,
        ITypeSymbol membersSource,
        ITypeSymbol? implementation = null,
        bool isDictionaryOwned = false)
    {
        var type = (implementation ?? membersSource).AsNonNullable();
        Symbol = membersSource;
        Types = types;
        Id = typeAId;

        IsTupleType = Symbol.IsTupleType;
        IsValueType = Symbol.IsValueType;
        IsInterface = Symbol.TypeKind is TypeKind.Interface;
        DictionaryOwned = isDictionaryOwned;
        Name = Symbol.Name;
        ShortName = Symbol.ToNameOnly();
        FullName = Symbol.ToGlobalNamespaced();

        ExportNonFullGenericName = type.ToGlobalNonGenericNamespace();
        FullNonGenericName = Symbol.ToGlobalNonGenericNamespace();
        IsKeyValueType = Name is "KeyValuePair";
        IsObject = SymbolEqualityComparer.Default.Equals(Symbol, types.Compilation.ObjectType);

        HasZeroArgsCtor =
            (type is INamedTypeSymbol { InstanceConstructors: { Length: > 0 } ctors } //Has instance constructors
             && ctors.Any(ctor => ctor.Parameters.IsDefaultOrEmpty && ctor.IsAccessible(Types.Compilation.SourceModule))) //With zero-args

            || implementation?.Kind is SymbolKind.ErrorType;

        (SanitizedName, ExportFullName) = membersSource.AsNonNullable() is { } memberSource
            ? (types.SanitizeName(memberSource), ExportFullName = memberSource.AsNonNullable().ToGlobalNamespaced().TrimEnd('?'))
            : (types.SanitizeName(type), ExportFullName = FullName);

        IsCollection = IsEnumerableType(FullNonGenericName, type, out Collection);

        Members = Set<MemberMeta>.Create(m => m.Id);

        if (IsTupleType) GetTupleMembers(out IsMemberless);
        else GetObjectMembers(out IsMemberless);
    }

    internal bool HasConversion(TypeMeta target, out ScalarConversion scalarConversion, out ScalarConversion reverseScalarConversion)
        => HasConversion(this, target, out scalarConversion) | HasConversion(target, this, out reverseScalarConversion);

    private bool HasConversion(TypeMeta source, TypeMeta target, out ScalarConversion info)
    {
        if ((source, target) is not (
            ({ IsTupleType: false }, { IsTupleType: false }) and
            ({ DictionaryOwned: false, IsKeyValueType: false }, { DictionaryOwned: false, IsKeyValueType: false })))
        {
            info = default;
            return false;
        }

        ITypeSymbol
            targetTypeSymbol = target.Symbol.AsNonNullable(),
            sourceTypeSymbol = source.Symbol;

        var conversion = Types.Compilation.ClassifyConversion(sourceTypeSymbol, targetTypeSymbol);

        info = (conversion.Exists && (source.IsValueType || source.FullName == "string" || source.IsObject),
            conversion.IsExplicit);

        if (!info.exists)
        {
            if (!info.isExplicit)
            {
                info.exists = info.isExplicit = sourceTypeSymbol
                    .GetMembers()
                    .Any(m =>
                        m is IMethodSymbol
                        {
                            MethodKind: MethodKind.Conversion,
                            Parameters: [{ Type: { } firstParam }],
                            ReturnType: { } returnType
                        }
                        && SymbolEqualityComparer.Default.Equals(returnType, sourceTypeSymbol)
                        && SymbolEqualityComparer.Default.Equals(firstParam, targetTypeSymbol)
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

    private bool HasConversion(TypeMeta sourceType, TypeMeta targetType)
    {
        if ((sourceType, targetType) is not (
            ({ IsTupleType: false }, { IsTupleType: false }) and
            ({ DictionaryOwned: false, IsKeyValueType: false }, { DictionaryOwned: false, IsKeyValueType: false })))
        {
            return false;
        }

        ITypeSymbol
            targetTypeSymbol = targetType.Symbol,
            sourceTypeSymbol = sourceType.Symbol;

        var conversion = Types.Compilation.ClassifyConversion(sourceTypeSymbol, targetTypeSymbol);

        ScalarConversion info = (
            conversion.Exists && (sourceType.IsValueType || sourceType.FullName == "string" || sourceType.IsObject),
            conversion.IsExplicit);

        if (!info.exists)
        {
            if (!info.isExplicit)
            {
                info.exists = info.isExplicit = sourceTypeSymbol
                    .GetMembers()
                    .Any(m =>
                        m is IMethodSymbol
                        {
                            MethodKind: MethodKind.Conversion,
                            Parameters: [{ Type: { } firstParam }],
                            ReturnType: { } returnType
                        }
                        && SymbolEqualityComparer.Default.Equals(returnType, sourceTypeSymbol)
                        && SymbolEqualityComparer.Default.Equals(firstParam, targetTypeSymbol)
                    );
            }
            else if (sourceType.IsInterface && sourceTypeSymbol.AllInterfaces.FirstOrDefault(targetType.Equals) is
            { } impl)
            {
                info.exists = !(info.isExplicit = false);
            }
        }

        info.exists |= info.isExplicit |= sourceType.IsObject && !targetType.IsObject;

        return info.exists;
    }

    private void GetObjectMembers(out bool isMemberless)
    {
        HashSet<int> ids = [];

        var isInterface = Symbol.TypeKind == TypeKind.Interface;

        GetMembers(Symbol);

        isMemberless = Members.Count == 0;

        return;

        void GetMembers(ITypeSymbol typeSymbol, bool isFirstLevel = true)
        {
            if (SymbolEqualityComparer.IncludeNullability.Equals(typeSymbol, Types.Compilation.ObjectType))
                return;

            var members = typeSymbol.GetMembers();

            if (members.IsDefaultOrEmpty) return;

            var i = 0;

            foreach (var member in members)
            {
                if (member is IFieldSymbol { AssociatedSymbol: IPropertySymbol assocProp })
                {
                    ids.Add(SymbolEqualityComparer.Default.GetHashCode(assocProp));
                    continue;
                }

                if (member.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Public))
                    continue;

                var isAccessible = member.DeclaredAccessibility is Accessibility.Public
                                   || SymbolEqualityComparer.Default.Equals(Types.Compilation.SourceModule, member.ContainingModule);

                switch (member)
                {
                    case IPropertySymbol
                    {
                        ContainingType.Name: not ['I', 'E', 'n', 'u', 'm', 'e', 'r', 'a', 't', 'o', 'r', ..],
                        IsIndexer: false,
                        IsImplicitlyDeclared: var impl,
                        Type: { } memberType,
                        IsStatic: false,
                    } prop when isInterface || !impl:

                        var id = SymbolEqualityComparer.Default.GetHashCode(member);

                        Members.TryAdd(
                            new(id,
                                prop,
                                Types.GetOrAdd(memberType),
                                this,
                                isAccessible,
                                ids.Contains(id),
                                i++));

                        continue;

                    case IFieldSymbol
                    {
                        ContainingType.Name: not ['I', 'E', 'n', 'u', 'm', 'e', 'r', 'a', 't', 'o', 'r', ..],
                        Type: { } memberType,
                        IsStatic: false
                    } field:

                        Members.TryAdd(
                            new(SymbolEqualityComparer.Default.GetHashCode(member),
                                field,
                                Types.GetOrAdd(memberType),
                                this,
                                isAccessible,
                                i++));

                        continue;

                    default:
                        continue;
                }
            }

            if (typeSymbol.BaseType != null)
                GetMembers(typeSymbol.BaseType);

            if (!isFirstLevel) return;

            foreach (var iface in typeSymbol.AllInterfaces)
                GetMembers(iface, false);
        }
    }

    private void GetTupleMembers(out bool isMemberless)
    {
        var module = Types.Compilation.SourceModule;

        var members = ((INamedTypeSymbol)Symbol.AsNonNullable()).TupleElements;

        if (isMemberless = members.IsDefaultOrEmpty) return;

        var i = 0;

        foreach (var member in members)

            Members.TryAdd(
                new(SymbolEqualityComparer.Default.GetHashCode(member),
                    member,
                    Types.GetOrAdd(member.Type),
                    this,
                    member.IsAccessible(Types.Compilation.SourceModule),
                    i++));
    }

    private bool IsEnumerableType(string nonGenericFullName, ITypeSymbol type, out CollectionMeta info)
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

    private CollectionMeta GetCollectionInfo(EnumerableType enumerableType, ITypeSymbol typeSymbol)
    {
        var itemDataType = Types.GetOrAdd((typeSymbol = typeSymbol.AsNonNullable()), null, enumerableType == EnumerableType.Dictionary);

        return enumerableType switch
        {
#pragma warning disable format
            EnumerableType.Dictionary =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
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
                    false,
                    "Enqueue",
                    "Count"),
            EnumerableType.Stack =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    true,
                    false,
                    "Push",
                    "Count"),
            EnumerableType.Enumerable =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
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
                    null,
                    "Length"),
            EnumerableType.Collection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.Span =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    null,
                    "Length"),
            _ =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    null,
                    "Length")
#pragma warning restore format
        };
    }
}
