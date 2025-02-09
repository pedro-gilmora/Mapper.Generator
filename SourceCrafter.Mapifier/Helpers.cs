﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.CodeAnalysis;


[assembly: InternalsVisibleTo("SourceCrafter.Mapifier.UnitTests")]
namespace SourceCrafter.Mapifier.Helpers
{
    public static class Extensions
    {
        private static readonly SymbolDisplayFormat
            GlobalizedNamespace = new(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters 
                                 | SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes 
                                      | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier),
            GlobalizedNonGenericNamespace = new(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes),
            SymbolNameOnly = new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly),
            TypeNameFormat = new(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters 
                                 | SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes 
                                      | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        internal static string ToGlobalNamespaced(this ISymbol t) => t.ToDisplayString(GlobalizedNamespace);
        
        internal static string ToGlobalNonGenericNamespace(this ISymbol t) => t.ToDisplayString(GlobalizedNonGenericNamespace);
       
        internal static string ToTypeNameFormat(this ITypeSymbol t) => t.ToDisplayString(TypeNameFormat);
        
        internal static string ToNameOnly(this ISymbol t) => t.ToDisplayString(SymbolNameOnly);
        
        internal static bool IsPrimitive(this ITypeSymbol target, bool includeObject = true) =>
            (includeObject && target.SpecialType is SpecialType.System_Object) || target.SpecialType is SpecialType.System_Enum
                or SpecialType.System_Boolean
                or SpecialType.System_Byte
                or SpecialType.System_SByte
                or SpecialType.System_Char
                or SpecialType.System_DateTime
                or SpecialType.System_Decimal
                or SpecialType.System_Double
                or SpecialType.System_Int16
                or SpecialType.System_Int32
                or SpecialType.System_Int64
                or SpecialType.System_Single
                or SpecialType.System_UInt16
                or SpecialType.System_UInt32
                or SpecialType.System_UInt64
                or SpecialType.System_String
            || target.Name is "DateTimeOffset" or "Guid"
            || (target.SpecialType is SpecialType.System_Nullable_T 
                && IsPrimitive(((INamedTypeSymbol)target).TypeArguments[0])); 
        
        internal static ITypeSymbol AsNonNullable(this ITypeSymbol type) =>
            type.Name == "Nullable"
                ? ((INamedTypeSymbol)type).TypeArguments[0]
                : type.WithNullableAnnotation(NullableAnnotation.None);

        internal static void TryGetNullable(this ITypeSymbol type, out ITypeSymbol outType, out bool outIsNullable)
                    => (outType, outIsNullable) = type.SpecialType is SpecialType.System_Nullable_T
                        || type is INamedTypeSymbol { Name: "Nullable" }
                            ? (((INamedTypeSymbol)type).TypeArguments[0], true)
                            : type.NullableAnnotation == NullableAnnotation.Annotated
                                ? (type.WithNullableAnnotation(NullableAnnotation.None), true)
                                : (type, false);

        internal static bool IsNullable(this ITypeSymbol typeSymbol)
            => typeSymbol.SpecialType is SpecialType.System_Nullable_T 
                || typeSymbol.NullableAnnotation == NullableAnnotation.Annotated 
                || typeSymbol is INamedTypeSymbol { Name: "Nullable" };
        
        internal static bool AllowsNull(this ITypeSymbol typeSymbol)
#if DEBUG
            => typeSymbol.BaseType?.ToGlobalNonGenericNamespace() is not ("global::System.ValueType" or "global::System.ValueTuple");
#else
            => typeSymbol is { IsValueType: false, IsTupleType: false, IsReferenceType: true };
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string Wordify(this string identifier, short upper = 0)
            => ToJoined(identifier, " ", upper);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static StringBuilder AddSpace(this StringBuilder sb, int count = 1) => sb.Append(new string(' ', count));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static StringBuilder CaptureGeneratedString(this StringBuilder code, Action action, out string expression)
        {
            int start = code.Length, end = code.Length;
            action();
            end = code.Length;
            var e = new char[end - start];
            code.CopyTo(start, e, 0, end - start);
            expression = new(e, 0, e.Length);
            return code;
        }

        private static string ToJoined(string identifier, string separator = "-", short casing = 0)
        {
            var buffer = new char[identifier.Length * (separator.Length + 1)];
            var bufferIndex = 0;

            for (var i = 0; i < identifier.Length; i++)
            {
                var ch = identifier[i];
                bool isLetterOrDigit = char.IsLetterOrDigit(ch), isUpper = char.IsUpper(ch);

                if (i > 0 && isUpper && char.IsLower(identifier[i - 1]))
                {
                    separator.CopyTo(0, buffer, bufferIndex, separator.Length);
                    bufferIndex += separator.Length;
                }
                if (isLetterOrDigit)
                {
                    buffer[bufferIndex++] = (casing, isUpper) switch
                    {
                        (1, false) => char.ToUpperInvariant(ch),
                        (-1, true) => char.ToLowerInvariant(ch),
                        _ => ch
                    };
                }
            }
            return new string(buffer, 0, bufferIndex);
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