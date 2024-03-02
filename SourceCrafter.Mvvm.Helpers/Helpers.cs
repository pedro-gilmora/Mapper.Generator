using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using SourceCrafter.Binding.Attributes;
[assembly: Bind<User, UserDto>]
namespace SourceCrafter.Binding.Attributes
{

    public enum Ignore { None, Source, This, Both }

#pragma warning disable CS9113 // Parameter is unread.

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class BindAttribute<TIn, TOut>(MappingKind kind = MappingKind.Normal, Ignore ignore = Ignore.None, string[] ignoreMembers = default!) : Attribute;

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class MaxDepthAttribute<TIn, TOut> : Attribute;

#pragma warning restore CS9113 // Parameter is unread.

    public enum MappingKind
    {
        Normal,
        Fill
    }

    public static class Extensions
    {

        private readonly static SymbolDisplayFormat
            _globalizedNamespace = new(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier),
            _globalizedNonGenericNamespace = new(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes),
            _symbolNameOnly = new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly),
            _typeNameFormat = new(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        internal static string ToGlobalizedNamespace(this ITypeSymbol t) => t.ToDisplayString(_globalizedNamespace);
        internal static string ToGlobalizedNonGenericNamespace(this ITypeSymbol t) => t.ToDisplayString(_globalizedNonGenericNamespace);
        internal static string ToTypeNameFormat(this ITypeSymbol t) => t.ToDisplayString(_typeNameFormat);
        internal static string ToNameOnly(this ISymbol t) => t.ToDisplayString(_symbolNameOnly);
        internal static bool IsPrimitive(this ITypeSymbol target, bool includeObject = true) =>
            target.IsValueType && !target.IsReferenceType
            || includeObject && target.SpecialType is SpecialType.System_Object or SpecialType.System_String;

        internal static bool IsNullable(this ITypeSymbol typeSymbol)
            => typeSymbol?.NullableAnnotation == NullableAnnotation.Annotated || typeSymbol is INamedTypeSymbol {Name : "Nullable" };

        internal static bool AllowsNull(this ITypeSymbol typeSymbol)
#if DEBUG
            => typeSymbol.BaseType?.ToGlobalizedNonGenericNamespace() is not ("global::System.ValueType" or "global::System.ValueTuple");
#else
            => typeSymbol is { IsValueType: false, IsTupleType: false, IsReferenceType: true };
#endif
    }


    public class User
    {
        public User Assistant { get; init; }
        public string FullName { get; set; }
        public int Age { get; set; }
        public User Supervisor { get; set; }
        public List<User>? Roles { get; set; }
    }
    public struct SupervisorDto
    {
        public string FullName { get; set; }
        public uint Age { get; set; }
        public List<UserDto?> Roles { get; set; }
    }

    public struct UserDto
    {
        public string FullName { get; set; }
        public uint Age { get; set; }
        public SupervisorDto Supervisor { get; init; }
        public List<SupervisorDto?> Roles { get; set; }
    }
}