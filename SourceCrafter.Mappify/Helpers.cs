using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.Mappify;


namespace SourceCrafter.Helpers
{
    public static class Extensions
    {
        internal const int HashPrime = 101;

        private static readonly SymbolDisplayFormat
            _globalizedNamespace = new(
                memberOptions:
                    SymbolDisplayMemberOptions.IncludeType |
                    SymbolDisplayMemberOptions.IncludeModifiers |
                    SymbolDisplayMemberOptions.IncludeExplicitInterface |
                    SymbolDisplayMemberOptions.IncludeParameters |
                    SymbolDisplayMemberOptions.IncludeContainingType |
                    SymbolDisplayMemberOptions.IncludeConstantValue |
                    SymbolDisplayMemberOptions.IncludeRef,
                globalNamespaceStyle:
                    SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle:
                    SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions:
                    SymbolDisplayGenericsOptions.IncludeTypeParameters |
                    SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions:
                    SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                    SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier,
                parameterOptions:
                    SymbolDisplayParameterOptions.IncludeType |
                    SymbolDisplayParameterOptions.IncludeModifiers |
                    SymbolDisplayParameterOptions.IncludeName |
                    SymbolDisplayParameterOptions.IncludeDefaultValue),
            _globalizedNonGenericNamespace = new(
                globalNamespaceStyle: 
                    SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: 
                    SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                miscellaneousOptions: 
                    SymbolDisplayMiscellaneousOptions.UseSpecialTypes),
            _symbolNameOnly = new(typeQualificationStyle: 
                    SymbolDisplayTypeQualificationStyle.NameOnly),
            _typeNameFormat = new(
                typeQualificationStyle: 
                    SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                genericsOptions: 
                    SymbolDisplayGenericsOptions.IncludeTypeParameters |
                    SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions: 
                    SymbolDisplayMiscellaneousOptions.UseSpecialTypes |                                      
                    SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        internal static string ToGlobalNamespaced(this ISymbol t) => t.ToDisplayString(_globalizedNamespace);

        internal static string ToGlobalNonGenericNamespace(this ISymbol t) =>
            t.ToDisplayString(_globalizedNonGenericNamespace);

        internal static string ToTypeNameFormat(this ITypeSymbol t) => t.ToDisplayString(_typeNameFormat);

        internal static string ToNameOnly(this ISymbol t) => t.ToDisplayString(_symbolNameOnly);

        static bool IsRelatedTo(this ITypeSymbol type, ITypeSymbol other)
        {
            return SymbolEqualityComparer.Default.Equals(type, other)
                   || type.HasBaseType(other)
                   || type.AllInterfaces.Any(type.HasBaseType);
        }

        static bool HasBaseType(this ITypeSymbol type, ITypeSymbol other)
        {
            return type is null || type.BaseType is null
                ? false
                : SymbolEqualityComparer.Default.Equals(type.BaseType, other) || HasBaseType(type.BaseType, other);
        }

        static IEnumerable<(IParameterSymbol, AttributeArgumentSyntax?)> GetAttrParamsMap(
            ImmutableArray<IParameterSymbol> paramSymbols,
            SeparatedSyntaxList<AttributeArgumentSyntax> argsSyntax)
        {
            int i = 0;
            foreach (var param in paramSymbols)
            {
                if (argsSyntax.Count > i && argsSyntax[i] is { NameColon: null, NameEquals: null } argSyntax)
                {
                    yield return (param, argSyntax);
                }
                else
                {
                    yield return (param,
                        argsSyntax.FirstOrDefault(arg => param.Name == arg.NameColon?.Name.Identifier.ValueText));
                }

                i++;
            }
        }

        public static bool IsAccessible(this ISymbol symbol, IModuleSymbol module) =>
            symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal 
            || SymbolEqualityComparer.Default.Equals(symbol.ContainingModule, module);

    private static bool GetStrExpressionOrValue(SemanticModel model, IParameterSymbol paramSymbol,
            AttributeArgumentSyntax? arg, out string value)
        {
            value = null!;

            if (arg is not null)
            {
                if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                    {
                        IsConst: true,
                        Type.SpecialType: SpecialType.System_String,
                        ConstantValue: { } val
                    })
                {
                    value = val.ToString();
                    return true;
                }
                else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } valueText } e
                         && e.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    value = valueText;
                    return true;
                }
            }
            else if (paramSymbol.HasExplicitDefaultValue)
            {
                value = paramSymbol.ExplicitDefaultValue?.ToString()!;
                return value != null;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToMetadataLongName(this ISymbol symbol)
        {
            var ret = new StringBuilder();

            foreach (var part in symbol.ToDisplayParts(_typeNameFormat))
            {
                if (part.Symbol is { Name: string name })
                    ret.Append(name.Capitalize());
                else
                    switch (part.ToString())
                    {
                        case ",":
                            ret.Append("And");
                            break;
                        case "<":
                            ret.Append("Of");
                            break;
                        case "[":
                            ret.Append("Array");
                            break;
                    }
            }

            return ret.ToString();
        }

        public static string ToMetadataLongName(this ISymbol symbol, Map<string, byte> uniqueName)
        {
            var existing = ToMetadataLongName(symbol);

            ref var count = ref uniqueName.GetValueOrAddDefault(existing, out var exists);

            if (exists)
            {
                return existing + "_" + (++count);
            }

            return existing;
        }

        public static string Capitalize(this string str)
        {
            return (str is [{ } f, .. { } rest] ? char.ToUpper(f) + rest : str);
        }

        public static string Camelize(this string str)
        {
            return (str is [{ } f, .. { } rest] ? char.ToLower(f) + rest : str);
        }

        public static string? Pascalize(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            ReadOnlySpan<char> span = str.AsSpan();
            Span<char> result = stackalloc char[span.Length];
            int resultIndex = 0;
            bool newWord = true;

            foreach (char c in span)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '_')
                {
                    newWord = true;
                }
                else
                {
                    if (newWord)
                    {
                        result[resultIndex++] = char.ToUpperInvariant(c);
                        newWord = false;
                    }
                    else
                    {
                        result[resultIndex++] = c;
                    }
                }
            }

            return result[0..resultIndex].ToString();
        }

        public static ImmutableArray<IParameterSymbol> GetParameters(this ITypeSymbol implType)
        {
            return implType is INamedTypeSymbol { Constructors: var ctor, InstanceConstructors: var insCtor }
                ? ctor.OrderBy(d => !d.Parameters.IsDefaultOrEmpty).FirstOrDefault()?.Parameters
                  ?? insCtor.OrderBy(d => !d.Parameters.IsDefaultOrEmpty).FirstOrDefault()?.Parameters
                  ?? ImmutableArray<IParameterSymbol>.Empty
                : ImmutableArray<IParameterSymbol>.Empty;
        }

        public static bool IsPrimitive(this ITypeSymbol target, bool includeObject = true) =>
            (includeObject && target.SpecialType is SpecialType.System_Object)
            || target.SpecialType is SpecialType.System_Enum
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

        public static ITypeSymbol AsNonNullable(this ITypeSymbol type) =>
            type.Name == "Nullable"
                ? ((INamedTypeSymbol)type).TypeArguments[0]
                : type.WithNullableAnnotation(NullableAnnotation.None);

        public static bool TryGetNullable(this ITypeSymbol type, out ITypeSymbol outType)
            => ((outType, _) = type.SpecialType is SpecialType.System_Nullable_T ||
                                          type is INamedTypeSymbol { Name: "Nullable" }
                ? (((INamedTypeSymbol)type).TypeArguments[0], true)
                : type.NullableAnnotation == NullableAnnotation.Annotated
                    ? (type.WithNullableAnnotation(NullableAnnotation.None), true)
                    : (type, false)).Item2;

        public static bool IsNullable(this ITypeSymbol typeSymbol)
            => typeSymbol.SpecialType is SpecialType.System_Nullable_T
               || typeSymbol.NullableAnnotation == NullableAnnotation.Annotated
               || typeSymbol is INamedTypeSymbol { Name: "Nullable" };

        public static bool IsNullable(this IPropertySymbol typeSymbol)
            => typeSymbol.Type.SpecialType is SpecialType.System_Nullable_T
               || typeSymbol.NullableAnnotation == NullableAnnotation.Annotated
               || typeSymbol is { Name: "Nullable" };

        public static bool IsNullable(this IFieldSymbol typeSymbol)
            => typeSymbol.Type.SpecialType is SpecialType.System_Nullable_T
               || typeSymbol.NullableAnnotation == NullableAnnotation.Annotated
               || typeSymbol is { Name: "Nullable" };

        public static bool AllowsNull(this ITypeSymbol typeSymbol)
            => typeSymbol is { IsValueType: false, IsTupleType: false, IsReferenceType: true };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StringBuilder AddSpace(this StringBuilder sb, int count = 1) => sb.Append(new string(' ', count));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StringBuilder CaptureGeneratedString(this StringBuilder code, Action action,
            out string expression)
        {
            int start = code.Length, end;
            action();
            end = code.Length;
            char[] e = new char[end - start];
            code.CopyTo(start, e, 0, end - start);
            expression = new(e, 0, e.Length);
            return code;
        }

        public static bool TryGetAsyncType(this ITypeSymbol typeSymbol, out ITypeSymbol factoryType)
        {
            switch ((factoryType = typeSymbol)?.ToGlobalNonGenericNamespace())
            {
                case "global::System.Threading.Tasks.ValueTask" or "global::System.Threading.Tasks.Task"
                    when factoryType is INamedTypeSymbol { TypeArguments: [{ } firstTypeArg] }:

                    factoryType = firstTypeArg;
                    return true;

                default:

                    return false;
            }

            ;
        }

        public static string RemoveDuplicates(this string? input)
        {
            if ((input = input?.Trim()) is null or "")
                return "";

            var result = "";
            int wordStart = 0;

            for (int i = 1; i < input.Length; i++)
            {
                if (char.IsUpper(input[i]))
                {
                    string word = input[wordStart..i];

                    if (!result.EndsWith(word))
                    {
                        result += word;
                    }

                    wordStart = i;
                }
            }

            string lastWord = input[wordStart..];

            if (!result.EndsWith(lastWord, StringComparison.OrdinalIgnoreCase))
            {
                result += lastWord;
            }

            return result;
        }

        public static string SanitizeTypeName(
            ITypeSymbol type,
            HashSet<string> methodsRegistry)
        {
            var id = Sanitize(type).Replace(" ", "").Capitalize();

            if (methodsRegistry.Add(id)) return id;

            var i = 0;

            while (!methodsRegistry.Add(id + ++i)) ;

            return id;

            static string Sanitize(ITypeSymbol type)
            {
                switch (type)
                {
                    case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

                        return "TupleOf" + string.Join("", els.Select(f => Sanitize(f.Type)));

                    case INamedTypeSymbol { IsGenericType: true, TypeParameters: var args }:

                        return type.Name + "Of" + string.Join("", args.Select(Sanitize));

                    default:

                        var typeName = type.ToTypeNameFormat();

                        if (type is IArrayTypeSymbol { ElementType: { } elType })
                            typeName = Sanitize(elType) + "Array";

                        return char.ToUpperInvariant(typeName[0]) + typeName.AsSpan()[1..].TrimEnd().ToString();
                }
            }
        }

        public static string Exchange(ref string oldVal, string newVal) => 
            oldVal.Equals(newVal) 
                ? oldVal 
                : ((oldVal, _) = (newVal, oldVal)).Item2;

        internal static ReadOnlySpan<int> Primes =>
        [
            3,
            7,
            11,
            17,
            23,
            29,
            37,
            47,
            59,
            71,
            89,
            107,
            131,
            163,
            197,
            239,
            293,
            353,
            431,
            521,
            631,
            761,
            919,
            1103,
            1327,
            1597,
            1931,
            2333,
            2801,
            3371,
            4049,
            4861,
            5839,
            7013,
            8419,
            10103,
            12143,
            14591,
            17519,
            21023,
            25229,
            30293,
            36353,
            43627,
            52361,
            62851,
            75431,
            90523,
            108631,
            130363,
            156437,
            187751,
            225307,
            270371,
            324449,
            389357,
            467237,
            560689,
            672827,
            807403,
            968897,
            1162687,
            1395263,
            1674319,
            2009191,
            2411033,
            2893249,
            3471899,
            4166287,
            4999559,
            5999471,
            7199369
        ];
    }
}


namespace SourceCrafter.Bindings
{
    public static class CollectionExtensions<T>
    {
        public static Collection<T> EmptyCollection => [];
        public static ReadOnlyCollection<T> EmptyReadOnlyCollection => new([]);
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