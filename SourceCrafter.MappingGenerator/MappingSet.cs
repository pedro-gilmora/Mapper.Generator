using System.Text;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using SourceCrafter.Mapping.Constants;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections;
using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq.Expressions;
using System.Data.SqlTypes;
using System.Diagnostics.CodeAnalysis;

namespace SourceCrafter.Mapping;

internal enum OutputType { Return, IterableCollector }

internal delegate void MappingBuilder(Indent indent, int rootId);
internal delegate StringBuilder FormattedAppend(
    [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
    params object?[] parameters
);

internal sealed class MappingSet
{

    private MappingDescriptor[] items = new MappingDescriptor[4];
    private int count = 0;
    private readonly StringBuilder code = new(@"namespace SourceCrafter.Mappings;

public static partial class Mappers
{");

    internal static INamedTypeSymbol objectTypeSymbol = null!;
    internal static readonly Func<ISymbol?, ISymbol?, bool> AreSymbolsEquals = SymbolEqualityComparer.Default.Equals;
    internal Func<string, StringBuilder> Append;
    internal FormattedAppend AppendFormatted;

    internal MappingSet()
    {
        Append = code.Append;
        AppendFormatted = code.AppendFormat;
    }

    public bool TryGetOrAdd(
        Compilation compilation,
        ITypeSymbol targetType,
        ITypeSymbol sourceType,
        string from,
        string to,
        out MappingDescriptor item,
        bool returnFound = true
    )
    {
        item = new(targetType, sourceType);
        if (count == 0)
        {
            items[0] = item;
            count++;

            return FindMappableMembers(compilation, item, from, to);
        }

        int
            low = 0,
            high = count - 1;


        while (low <= high)
        {
            int mid = low + (high - low) / 2;

            switch (items[mid].CompareTo(item))
            {
                case 0:
                    if (!returnFound)
                        return false;

                    return (item = items[mid]).IsMappable;
                case < 0:
                    low = mid + 1;
                    continue;
                default:
                    high = mid - 1;
                    continue;
            }
        }

        if (count == items.Length)
            Array.Resize(ref items, items.Length * 2);

        if (low < count)
            Array.Copy(items, low, items, low + 1, count - low);

        items[low] = item;
        count++;

        return FindMappableMembers(compilation, item, from, to);
    }
    public bool Contains(
        ITypeSymbol targetType,
        ITypeSymbol sourceType
    )
    {
        MappingDescriptor item = new(targetType, sourceType);
        //if (count == 0)
        //{
        //    items[0] = item;
        //    count++;

        //    return FindMappableMembers(compilation, ignore, item); ;
        //}

        int
            low = 0,
            high = count - 1;


        while (low <= high)
        {
            int mid = low + (high - low) / 2;

            switch (items[mid].CompareTo(item))
            {
                case 0:
                    return true;
                case < 0:
                    low = mid + 1;
                    continue;
                default:
                    high = mid - 1;
                    continue;
            }
        }

        //if (count == items.Length) 
        //    Array.Resize(ref items, items.Length * 2);

        //if (low < count) 
        //    Array.Copy(items, low, items, low + 1, count - low);

        //items[low] = item;        
        //count++;

        return false;
    }


    internal void BuildType(
        CodeGenerator codeGen,
        Indent indent,
        int rootId,
        string typeB,
        string from = "from",
        string to = "to"
    )
    {
        if (codeGen.PreInitialize is { } preInit)
            preInit(indent, rootId);

        AppendFormatted(@"
{0}    var {1} = new {2}
{0}    {{", indent, to, typeB);

        codeGen.Comma = null;
        ++indent;

        codeGen.AddMember(indent, rootId);

        --indent;

        AppendFormatted(@"
{0}    }};", indent);
    }

//    private void AddEnumerableMapping(
//        CodeGenerator parentCodeGen,
//        Compilation compilation,
//        Indent indent,
//        string inExpression,
//        bool inNullable,
//        string outExpression,
//        string outTypeName,
//        IterInfo _in,
//        IterInfo _out,
//        int rootId)
//    {
//        bool hasMapping;
//        CodeGenerator codeGen;

//        string
//            inCollVarName = inExpression.Replace(".", ""),
//            outCollVarName = outExpression.Replace(".", ""),
//            inItemVarName = $"{inCollVarName}Item",
//            outItemVarName = $"{outCollVarName}Item";

//        switch ((_in.IterableType, _out.IterableType))
//        {
//            case (not IterableType.Enumerable, not IterableType.Collection):
//                #region Countable to array

//                string
//                    inCollItemVarName = $"{inCollVarName}[{inCollVarName}Ix]",
//                    outColltItemVarName = $"{outCollVarName}[{inCollVarName}Ix]";

//                if (!NeedsConverter(
//                        compilation,
//                        _in.Type,
//                        _out.Type,
//                        _out.ItemFullTypeName,
//                        inItemVarName,
//                        ref inCollItemVarName,
//                        out hasMapping,
//                        out codeGen)
//                    || !(hasMapping
//                        && (inNullable
//                            ? codeGen.CanCreateNullableMapper(rootId)
//                            : codeGen.CanCreateMapper(rootId))))

//                    return;
//                parentCodeGen.PreInitialize += (indent, from, to, rootId) =>
//                {
//                    AppendFormatted(@"
//{0}    #region Translating from {1} to {2}

//{0}    var {3} = {1};
//{0}    var {4} = new {5}[{3}.{6}];

//{0}    for(int {3}Ix = 0, {3}Len = {3}.{6}; {3}Ix < {3}Len; {3}Ix++) 
//{0}    {{",
//                        indent,
//                        inExpression,
//                        outExpression,
//                        inCollVarName,
//                        outCollVarName,
//                        _out.ItemFullTypeName,
//                        _in.CountProperty);
//                };
//                indent++;

//                if (hasMapping)
//                {
//                    if (inNullable)
//                    {
//                        parentCodeGen.PreInitialize += (indent, from, to, rootId) =>
//                        {
//                            AppendFormatted(@"
//{0}    if ({1} is not {{}} {2}) 
//{0}    {{
//{0}        {3} = default;
//{0}        continue;
//{0}    }}
//",
//                            indent,
//                            inCollItemVarName,
//                            inItemVarName,
//                            outColltItemVarName);
//                        };
//                    }
//                    else
//                    {
//                        parentCodeGen.PreInitialize += (indent, from, to, rootId) =>
//                        {
//                            AppendFormatted(@"
//{0}    var {1} = {2};",
//                            indent,
//                            inItemVarName,
//                            inCollItemVarName);
//                        };
//                    }

//                    BuildType(codeGen, indent, rootId, _out.ItemFullTypeName.TrimEnd('?'), inItemVarName, outItemVarName);
//                }
//                else
//                {
//                    outItemVarName = inCollItemVarName;
//                }

//                indent--;

//                AppendFormatted(@"
//{0}        {1} = {2};
//{0}    }}

//{0}    {3} = {4};
//{0}    #endregion
//", indent, outColltItemVarName, outItemVarName, outExpression, outCollVarName);

//                #endregion
//                break;
//            case (_, IterableType.Collection):
//                #region Any to collection

//                if (!NeedsConverter(
//                        compilation,
//                        _in.Type,
//                        _out.Type,
//                        _out.ItemFullTypeName,
//                        inItemVarName,
//                        ref outItemVarName,
//                        out hasMapping,
//                        out codeGen)
//                    || !(hasMapping
//                        && (inNullable
//                            ? codeGen.CanCreateNullableMapper(rootId)
//                            : codeGen.CanCreateMapper(rootId))))

//                    return;

//                AppendFormatted(@"
//{0}    #region Translating from {1} to {2}

//{0}    var {3} = {1};
//{0}    var {4} = new {5}();

//{0}    foreach (var {6} in {3})
//{0}    {{",
//                indent,
//                    inExpression,
//                    outExpression,
//                    inCollVarName,
//                    outCollVarName,
//                    outTypeName.TrimEnd('?'),
//                    inItemVarName);

//                indent++;

//                if (hasMapping)
//                {
//                    if (inNullable)
//                    {
//                        AppendFormatted(@"
//{0}    if ({1} is null) 
//{0}    {{
//{0}        {2}
//{0}        continue;
//{0}    }}
//",
//                            indent,
//                            inItemVarName,
//                            string.Format(_out.AddMethod, outCollVarName, "default"));
//                    }

//                    BuildType(codeGen, indent, rootId, _out.ItemFullTypeName.TrimEnd('?'), inItemVarName, outItemVarName);
//                }

//                indent--;

//                AppendFormatted(@"
//{0}        {1}
//{0}    }}

//{0}    {2} = {3};

//{0}    #endregion
//",
//                    indent,
//                    string.Format(_out.AddMethod, outCollVarName, outItemVarName),
//                    outExpression,
//                    outCollVarName);
//                #endregion
//                break;
//            case (_, IterableType.Enumerable):
//                #region Any to collection

//                if (!NeedsConverter(
//                        compilation,
//                        _in.Type,
//                        _out.Type,
//                        _out.ItemFullTypeName,
//                        inItemVarName,
//                        ref outItemVarName,
//                        out hasMapping,
//                        out codeGen)
//                    || (hasMapping
//                        && (inNullable
//                            ? !codeGen.CanCreateNullableMapper(rootId)
//                            : !codeGen.CanCreateMapper(rootId))))

//                    return;

//                AppendFormatted(@"
//{0}    #region Translating from {1} to {2}

//{0}    var {3} = {1};
//{0}    var {4} = new global::System.Collections.Generic.List<{5}>();

//{0}    foreach (var {6} in {3})
//{0}    {{", indent, inExpression, outExpression, inCollVarName, outCollVarName, _out.ItemFullTypeName, inItemVarName);

//                indent++;

//                if (hasMapping)
//                {
//                    if (inNullable)
//                    {
//                        AppendFormatted(@"
//{0}    if ({1} is null) 
//{0}    {{
//{0}        {2}.Add(default);
//{0}        continue;
//{0}    }}",
//                            indent,
//                            inItemVarName,
//                            outCollVarName);
//                    }

//                    BuildType(codeGen, indent, rootId, _out.ItemFullTypeName.TrimEnd('?'), inItemVarName, outItemVarName);
//                }

//                indent--;

//                AppendFormatted(@"
//{0}        {1}.Add({2});
//{0}    }}

//{0}    {3} = {4};

//{0}    #endregion
//",
//                    indent,
//                    outCollVarName,
//                    outItemVarName,
//                    outExpression,
//                    outCollVarName);
//                #endregion
//                break;
//            case (_, IterableType.Array or IterableType.Enumerable):
//                #region Any to array

//                inCollItemVarName = $"{inCollVarName}[{inCollVarName}Ix]";
//                outColltItemVarName = $"{outCollVarName}[{inCollVarName}Ix]";

//                if (!NeedsConverter(
//                    compilation,
//                    _in.Type,
//                    _out.Type,
//                    _out.ItemFullTypeName,
//                    inItemVarName,
//                    ref outItemVarName,
//                    out hasMapping,
//                    out codeGen)
//                    || (hasMapping
//                        && (inNullable
//                            ? !codeGen.CanCreateNullableMapper(rootId)
//                            : !codeGen.CanCreateMapper(rootId)))
//                )
//                    return;

//                AppendFormatted(@"
//{0}    #region Translating from {1} to {2}

//{0}    var {3} = {1};
//{0}    var {4} = new {5}[4];
//{0}    var {4}Count = 0;
//{0}    var {3}Ix = 0;

//{0}    foreach (var {6} in {3})
//{0}    {{
//{0}        if ({4}Count == {4}.Length) 
//{0}            global::System.Array.Resize(ref {4}, {4}.Length * 2);
//",
//                indent,
//                inExpression,
//                outExpression,
//                inCollVarName,
//                outCollVarName,
//                _out.ItemFullTypeName,
//                inItemVarName);

//                indent++;

//                if (hasMapping)
//                {
//                    if (inNullable)
//                    {
//                        AppendFormatted(@"
//{0}    if ({1} is not {{}} {2}) 
//{0}    {{
//{0}        {3} = default;
//{0}        continue;
//{0}    }}
//",
//                            indent,
//                            inCollItemVarName,
//                            inItemVarName,
//                            outColltItemVarName);
//                    }
//                    else
//                    {
//                        AppendFormatted(@"
//{0}    var {1} = {2};",
//                            indent,
//                            inItemVarName,
//                            inCollItemVarName);
//                    }

//                    BuildType(codeGen, indent, rootId, _out.ItemFullTypeName.TrimEnd('?'), inItemVarName, outItemVarName);
//                }
//                else
//                {
//                    outItemVarName = inCollItemVarName;
//                }

//                AppendFormatted(@"
//{0}    {1}[{2}Ix++] = {3};", indent, outCollVarName, inCollVarName, outItemVarName);

//                indent--;

//                AppendFormatted(@"
//{0}    }}

//{0}    if ({2}Count < {2}.Length) 
//{0}        global::System.Array.Resize(ref {2}, {2}Count);

//{0}    {1} = {2};

//{0}    #endregion
//", indent, outExpression, outCollVarName);
//                #endregion
//                break;
//        }


//    }

    bool NeedsConverter(
        Compilation compilation,
        ITypeSymbol _in,
        ITypeSymbol _out,
        string outItemFullTypeName,
        string inItemVarName,
        ref string outItemVarName,
        string from,
        string to,
        out bool hasMapping,
        out CodeGenerator map)
    {
        hasMapping = false;
        map = null!;

        switch (compilation.ClassifyConversion(_in, _out))
        {
            case { IsExplicit: true, IsReference: var isRef }:

                if (isRef)
                    outItemVarName = $"__FROM__.{inItemVarName} as {outItemFullTypeName.TrimEnd('?')}";
                else
                    outItemVarName = $"({outItemFullTypeName})__FROM__.{inItemVarName}";

                return false;
            case { Exists: false }:

                return hasMapping = 
                    TryGetOrAdd(compilation, _in, _out, from, to, out var mapper) 
                    && (map = mapper[_in]!) is { CanBuild: true };

        }

        return false;
    }

//    internal void AddMember(Indent indent, int rootId, Ignore ignoreValue, MemberMappingInfo info, StringBuilder code, bool isInit, string inExpression, string outExpression)
//    {
//        switch (info.Compilation.ClassifyConversion(info.InMemberType, info.OutMemberType))
//        {
//            case { IsExplicit: true, IsReference: var isRef }:

//                AppendFormatted(@"
//{0}    {1} = {2};",
//                    indent,
//                    outExpression,
//                    isRef
//                        ? $"{inExpression} as {info.OutTypeName.TrimEnd('?')}"
//                        : $"({info.OutTypeName}){inExpression}");

//                return;
//            case { Exists: false }:

//                CodeGenerator map;

//                if (!TryGetOrAdd(
//                        info.Compilation,
//                        info.InMemberType,
//                        info.OutMemberType,
//                        out var mapper)
//                    || (map = mapper[info.InMemberType]!) is null)
//                {
//                    AppendFormatted(@"
//{0}    /* Can't convert from 
//{0}       {1} ({2}) 
//{0}       into 
//{0}       {3} ({4}); */", indent, inExpression, info.InTypeName, outExpression, info.OutTypeName);

//                    return;
//                }

//                string
//                    from = inExpression.Replace(".", ""),
//                    to = outExpression.Replace(".", "");

//                if (info.IsInNullable && map.CanCreateNullableMapper(rootId))
//                {
//                    AppendFormatted(@"
//{0}    if ({2} is {{}} {1})
//{0}    {{", indent, from, inExpression);

//                    indent++;
//                    //Build mapper
//                    BuildType(map, indent, rootId, info.OutTypeName.TrimEnd('?'), from, to);

//                    AppendFormatted(@"
//{0}    {1} = {2};", indent, outExpression, to);

//                    indent--;
//                    AppendFormatted(@"
//{0}    }}", indent, from, inExpression);
//                }
//                else if (!info.IsInNullable && map.CanCreateMapper(rootId))
//                {
//                    AppendFormatted(@"
//{0}    var {1} = {2};", indent, from, inExpression);

//                    //Build mapper
//                    BuildType(map, indent, rootId, info.OutTypeName.TrimEnd('?'), from, to);

//                    AppendFormatted(@"
//{0}    {1} = {2};", indent, outExpression, to);
//                }

//                return;
//        }

//        if (isInit)
//            AppendFormatted(@"
//{0}    {1} = {2}", indent, outExpression, inExpression);

//        else
//            AppendFormatted(@"
//{0}    {1} = {2};", indent, outExpression, inExpression);
//    }

    static bool IsEnumerable(ITypeSymbol type, string globalizedGenericName, out IterInfo output)
    {
        output = new();

        if (type.SpecialType == SpecialType.System_String || type.IsPrimitive())

            return false;

        switch (globalizedGenericName)
        {
            case "global::System.Collections.Generic.Stack"
            :
                output.AddMethod = @"{0}.Push({1});";

                output.CountProperty = "Count";

                output.ItemFullTypeName =
                    (output.Type = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? (objectTypeSymbol))
                        .ToGlobalizedNamespace();

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                output.AddMethod = @"{0}.Enqueue({1});";

                output.CountProperty = "Count";
                output.ItemFullTypeName =
                    (output.Type = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol)
                    .ToGlobalizedNamespace();

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                output.AddMethod = @"{0}.Add({1});";

                output.ItemFullTypeName =
                    (output.Type = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol)
                    .ToGlobalizedNamespace();
                output.CountProperty = "Count";

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                output.AddMethod = null!;
                output.CountProperty = "Count";
                output.ItemFullTypeName =
                    (output.Type = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol)
                    .ToGlobalizedNamespace();

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                output.AddMethod = null!;
                output.ItemFullTypeName =
                    (output.Type = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() ?? objectTypeSymbol)
                    .ToGlobalizedNamespace();

                output.IterableType = IterableType.Enumerable;

                return true;

            default:
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                {
                    output.ItemFullTypeName =
                        (output.Type = elType)
                        .ToGlobalizedNamespace();

                    output.CountProperty = "Length";
                    output.IterableType = IterableType.Array;

                    return true;
                }
                else
                    foreach (var item in type.AllInterfaces)
                        if (IsEnumerable(item, item.ToGlobalizedNonGenericNamespace(), out output))
                            return true;
                output.Type = objectTypeSymbol;
                break;
        }

        return false;
    }

    internal bool FindMappableMembers(
        Compilation compilation,
        MappingDescriptor desc,
        string from,
        string to)
    {
        if ((desc.Mapped && !desc.IsMappable)
            || desc.TypeA.IsPrimitive()
            || desc.TypeB.IsPrimitive()
            || !GetMappableMembers(desc.TypeA, out var inMembers)
            || !GetMappableMembers(desc.TypeB, out var outMembers))

            return false;

        foreach (var inMember in inMembers)
        {
            foreach (var outMember in outMembers)
            {
                string
                    inMemberName = inMember.ToNameOnly(),
                    outMemberName = outMember.ToNameOnly();
                bool
                    nameEquals = inMemberName == outMemberName,
                    isFromToMappable = AreMappablesByAttribute(compilation, inMember, outMember, out var ignoreSource),
                    isToFromMappable = AreMappablesByAttribute(compilation, outMember, inMember, out var ignoreTarget);

                if (ignoreSource == Ignore.Both || ignoreTarget == Ignore.Both)
                    continue;

                var inMemberType = (inMember as IPropertySymbol)?.Type ?? ((IFieldSymbol)inMember).Type;
                var outMemberType = (outMember as IPropertySymbol)?.Type ?? ((IFieldSymbol)outMember).Type;

                var inMemberTypeName = inMemberType.ToGlobalizedNamespace();
                var outMemberTypeName = outMemberType.ToGlobalizedNamespace();

                string inNonGenericTypeName = inMemberType.ToGlobalizedNonGenericNamespace();
                string outNonGenericTypeName = outMemberType.ToGlobalizedNonGenericNamespace();

                var inNullable = inMemberType.IsNullable();
                var outNullable = outMemberType.IsNullable();

                if (TryDefineMapper(
                        inMember,
                        outMember,
                        inMemberType,
                        outMemberType,
                        from, 
                        to,
                        inMemberName,
                        outMemberName,
                        inNonGenericTypeName,
                        outNonGenericTypeName,
                        outMemberTypeName,
                        inNullable,
                        nameEquals,
                        isFromToMappable,
                        ignoreTarget,
                        desc.AMapper) 
                    | // Performs both comparison
                    TryDefineMapper(
                        outMember,
                        inMember,
                        outMemberType,
                        inMemberType,
                        to,
                        from,
                        outMemberName,
                        inMemberName,
                        outNonGenericTypeName,
                        inNonGenericTypeName,
                        inMemberTypeName,
                        outNullable,
                        nameEquals,
                        isToFromMappable,
                        ignoreSource,
                        desc.BMapper))
                    break;
            }
        }

        bool TryDefineMapper(
            ISymbol inMember,
            ISymbol outMember,
            ITypeSymbol inMemberType,
            ITypeSymbol outMemberType,
            string from,
            string to,
            string inMemberName,
            string outMemberName,
            string inNonGenericTypeName,
            string outNonGenericTypeName,
            string outMemberTypeName,
            bool inNullable,
            bool nameEquals,
            bool isMappable,
            Ignore ignoreTarget,
            CodeGenerator codeGen)
        {
            if (ignoreTarget != Ignore.Target && (nameEquals || isMappable) 
                && AreMappablesByDesign(inMember, outMember))
            {
                desc.IsMappable |= true;
                codeGen.CanBuild |= true;

                //Determine conversion type: complex, iterable or just assignable (might imply conversion)
                bool hasMapping = false;

                if (IsEnumerable(inMemberType, inNonGenericTypeName, out var _in) &&
                         IsEnumerable(outMemberType, outNonGenericTypeName, out var _out))
                {
                    codeGen.PreInitialize += (indent, rootId) =>
                    {
                        AppendFormatted(@"
{0}    //Should generate a mapping iterator for {1}({2})", indent, outMemberName, outMemberTypeName);
                    };

                }
                else if (NeedsConverter(compilation, inMemberType, outMemberType, outMemberTypeName, inMemberName, ref inMemberName, from, to, out hasMapping, out var subCodeGen))
                {
                    if (hasMapping)
                    {
                        codeGen.PreInitialize += (indent, _) =>
                        {
                            if (inNullable && codeGen.CanCreateNullableMapper(codeGen.id))
                            {
                                AppendFormatted(@"
{0}    if ({1} is {{}} {2})
{0}    {{", indent, from + '.' + inMemberName, from + inMemberName);

                                indent++;

                                BuildType(subCodeGen, indent, codeGen.id, outMemberTypeName, from + inMemberName, to + outMemberName);

                                indent--;

                                AppendFormatted(@"
{0}    }}", indent);
                            }
                            else if(!inNullable && codeGen.CanCreateMapper(codeGen.id))
                            {
                                BuildType(subCodeGen, indent, codeGen.id, outMemberTypeName, from + inMemberName, to + outMemberName);
                            }
                        };
                    }
                }
                
                codeGen.AddMember += (indent, rootId) =>
                {
                    var to2 = from + '.' + inMemberName;

                    if (hasMapping)
                        to2 = from + inMemberName;

                    AppendFormatted(@"{0}
{1}    {2} = {3}", codeGen.Comma, indent, outMemberName, from + '.' + inMemberName);

                    codeGen.Comma ??= ",";
                };

            }
            return false;
        }

        //AddEnumerableMapping(info, indent, inExpression, outExpression, _in, _out, rootId)


        //                if (inMember is IPropertySymbol { SetMethod.IsInitOnly: true })
        //                {
        //                    if (!inMemberType.IsPrimitive(true))
        //                    {
        //                        mapper.PreInitialize += (inVarName, outVarName, indent, rootId) =>
        //                        {
        //                            BuildType(
        //                                mapper,
        //                                indent,
        //                                rootId,
        //                                outMemberTypeName.TrimEnd('?'),
        //                                inNullable,
        //                                inVarName + '.' + inMemberName,
        //                                outVarName.Replace(".", "") + outMemberName);
        //                        };
        //                        mapper.Initialize += (inVarName, outVarName, indent, rootId) =>
        //                        {
        //                            AppendFormatted(@"{0}
        //{1}   {2} = {3}",
        //                                mapper.Comma,
        //                                indent,
        //                                outVarName + '.' + outMemberName,
        //                                outVarName.Replace(".", "") + outMemberName);
        //                            mapper.Comma ??= ",";
        //                        };
        //                    }
        //                    else
        //                        mapper.Initialize += (inVarName, outVarName, indent, rootId) =>
        //                        {
        //                            Append(mapper.Comma);
        //                            BuildMember(
        //                                mapping,
        //                                parentIgnore,
        //                                code,
        //                                indent,
        //                                inVarName,
        //                                outVarName,
        //                                true,
        //                                rootId);
        //                            mapper.Comma ??= ",";
        //                        };
        //                }
        //                else
        //                    mapper.Build += (inVarName, outVarName, indent, rootId) =>
        //                    {
        //                        BuildMember(
        //                            mapping,
        //                            parentIgnore,
        //                            code,
        //                            indent,
        //                            inVarName,
        //                            outVarName,
        //                            false,
        //                            rootId);
        //                    };
        //                return true;
        //        if (item.IsMappable)
        //        {
        //            if (outNullable)
        //            {
        //                item.AMapper.Build += (_, _, indent, _) =>
        //                    AppendFormatted(@"
        //{0}    }}", indent);
        //            }

        //            if (inNullable)
        //            {
        //                item.BMapper.Build += (_, _, indent, _) =>
        //                    AppendFormatted(@"
        //{0}    }}", indent);
        //            }
        //        }

        desc.Mapped = true;
        return desc.IsMappable;
    }
    static bool AreMappablesByAttribute(Compilation compilation, ISymbol target, ISymbol source, out Ignore ignoreSource)
    {
        ignoreSource = Ignore.None;
        var matches = false;

        foreach (var attr in target.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() is not { } className) continue;

            if (className == "SourceCrafter.Mapping.Attributes.IgnoreAttribute"
                && (ignoreSource = (Ignore)(int)attr.ConstructorArguments[0].Value!) != Ignore.Source) return false;

            if (className != "SourceCrafter.Mapping.Attributes.MapAttribute") continue;

            if ((ignoreSource = (Ignore)(int)attr.ConstructorArguments[1].Value!) is not (Ignore.Source or Ignore.None))
                return false;

            if ((attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0].Expression
                is not InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                }
                || compilation.GetSemanticModel(id.SyntaxTree)?.GetSymbolInfo(id).Symbol is not ISymbol member) continue;

            matches |= AreSymbolsEquals(member, source);
        }

        return matches;
    }
    private bool AreMappablesByDesign(ISymbol target, ISymbol source) =>
        (target is IPropertySymbol { IsReadOnly: false } or IFieldSymbol { IsReadOnly: false }
        && target.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
        && source is
            IPropertySymbol { IsWriteOnly: false, DeclaredAccessibility: Accessibility.Public or Accessibility.Internal }
            or IFieldSymbol { DeclaredAccessibility: Accessibility.Public or Accessibility.Internal });

    static bool GetMappableMembers(ITypeSymbol? target, out ImmutableList<ISymbol>.Builder members)
    {
        var _members = members = ImmutableList.CreateBuilder<ISymbol>();

        readMembers(target);

        return _members.Count > 0;

        void readMembers(ITypeSymbol? target)
        {
            if (target == null || target.IsPrimitive())
                return;

            readMembers(target.BaseType);

            foreach (var member in target.GetMembers())

                if (member is IPropertySymbol { IsIndexer: false } or IFieldSymbol { AssociatedSymbol: null })

                    _members.Add(member);
        }
    }

    static int lastId = 0;
    static int NextId() => ++lastId;

    internal void GenerateNullableMethod(
        CodeGenerator handler,
        Indent indent,
        string methodName,
        string typeA,
        string typeB)
    {
        AppendFormatted(@"
    public static bool {0}(this {1} from, out {2} to) 
    {{
        to = default;

        if (from is null) return false;
", methodName, typeA, typeB);

        BuildType(handler, indent, NextId(), typeB);

        Append(@"
        return true;
    }");

    }

    internal void GenerateMethod(
        CodeGenerator handler,
        Indent indent,
        string methodName,
        string typeA,
        string typeB
    ){
        AppendFormatted(@"
    public static {2} {0}(this {1} from)
    {{", methodName, typeA, typeB);

        BuildType(handler, indent, NextId(), typeB);

        Append(@"
        return to;
    }");
    }

    public override string ToString() => code.ToString();
}