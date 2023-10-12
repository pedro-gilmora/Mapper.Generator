// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceCrafter.Mapping.Constants;
using System.ComponentModel;

namespace SourceCrafter.Mapping
{
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

        internal static bool HasConversion(this MappingSet owningSet, Compilation comp, Ignore ignore, ITypeSymbol _in, ITypeSymbol _out, string outItemFullTypeName, string inItemVarName, ref string outItemVarName, out bool hasMapping, out CodeGenerator map)
        {
            hasMapping = false;
            map = null!;

            switch (comp.ClassifyConversion(_in, _out))
            {
                case { IsExplicit: true, IsReference: var isRef, }:

                    if (isRef)
                        outItemVarName = $"{inItemVarName} as {outItemFullTypeName.TrimEnd('?')}";
                    else
                        outItemVarName = $"({outItemFullTypeName}){inItemVarName}";

                    break;
                case { Exists: false }:

                    return hasMapping = owningSet.TryGetOrAdd(comp, _in, _out, out var mapper)
                        && (map = mapper[_in]!) is not null;
            }

            return true;
        }
    }
}

#if NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP2_0 || NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0 || NETCOREAPP3_1 || NET45 || NET451 || NET452 || NET6 || NET461 || NET462 || NET47 || NET471 || NET472 || NET48


// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata.
    /// This class should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}

#endif
#if !NET7_0_OR_GREATER

// The namespace is important
namespace System.Diagnostics.CodeAnalysis
{

    /// <summary>Fake version of the StringSyntaxAttribute, which was introduced in .NET 7</summary>
    public sealed class StringSyntaxAttribute : Attribute
    {
        /// <summary>The syntax identifier for strings containing composite formats.</summary>
        public const string CompositeFormat = nameof(CompositeFormat);

        /// <summary>The syntax identifier for strings containing regular expressions.</summary>
        public const string Regex = nameof(Regex);

        /// <summary>The syntax identifier for strings containing date information.</summary>
        public const string DateTimeFormat = nameof(DateTimeFormat);

        /// <summary>The syntax identifier for strings containing date information.</summary>
        public object?[] Arguments = { };
        public string Syntax { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StringSyntaxAttribute"/> class.
        /// </summary>
        public StringSyntaxAttribute(string syntax)
        {
            Syntax = syntax;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StringSyntaxAttribute"/> class.
        /// </summary>
        public StringSyntaxAttribute(string syntax, params object?[] arguments)
        {
            Syntax = syntax;
            Arguments = arguments;
        }
    }
}
#endif

namespace System.Runtime.CompilerServices
{

#if !NET7_0_OR_GREATER

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }
        public bool IsOptional { get; init; }

        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }

#endif // !NET7_0_OR_GREATER
}

namespace System.Diagnostics.CodeAnalysis
{
#if !NET7_0_OR_GREATER
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
#endif
}