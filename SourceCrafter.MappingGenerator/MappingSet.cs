using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using SourceCrafter.Bindings.Helpers;

namespace SourceCrafter.MappingGenerator;

internal delegate string ValueBuilder(string val);

delegate string BuildMember(
    ValueBuilder createType,
    TypeData targetType,
    Member targetMember,
    TypeData sourceType,
    Member sourceMember);

internal sealed partial class MappingSet(Compilation compilation, StringBuilder code)
{
    short targetScopeId, sourceScopeId;

    readonly TypeSet typeSet = new(compilation);

    readonly bool canOptimize = compilation.GetTypeByMetadataName("System.Span`1") is not null,
        canUseUnsafeAccessor = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;

    private const string
        TUPLE_START = "(",
        TUPLE_END = " )",
        TYPE_START = @"new {0}
        {{",
        TYPE_END = @"
        }";
    static readonly EqualityComparer<uint> _uintComparer = EqualityComparer<uint>.Default;

    internal static readonly SymbolEqualityComparer
        _comparer = SymbolEqualityComparer.Default;

    internal Action AddMapper(TypeMapInfo sourceType, TypeMapInfo targetType, ApplyOn ignore, MappingKind mapKind)
    {
        ITypeSymbol targetTypeSymbol = targetType.implementation ?? targetType.membersSource,
            sourceTypeSymbol = sourceType.implementation ?? sourceType.membersSource;

        int targetId = GetId(targetTypeSymbol),
            sourceId = GetId(sourceTypeSymbol);

        Member
            target = new(++targetScopeId, "to", targetTypeSymbol.IsNullable()),
            source = new(--sourceScopeId, "source", sourceTypeSymbol.IsNullable());

        var typeMappingInfo = GetOrAddMapper(
            targetType,
            sourceType,
            targetId,
            sourceId);

        typeMappingInfo.MappingsKind = mapKind;

        typeMappingInfo = CreateType(
            typeMappingInfo,
            source,
            target,
            ignore,
            "+",
            "+");

        return typeMappingInfo.BuildMethods;
    }

    TypeMappingInfo CreateType
    (
        TypeMappingInfo map,
        Member source,
        Member target,
        ApplyOn ignore,
        string sourceMappingPath,
        string targetMappingPath)
    {
        map.EnsureDirection(ref target, ref source);

        GetNullability(target, map.TargetType._typeSymbol, source, map.SourceType._typeSymbol);

        if (map.CanMap is not null)
        {
            if (!map.TargetType.IsRecursive && !map.TargetType.IsStruct && !map.TargetType.IsPrimitive)
                map.TargetType.IsRecursive = IsRecursive(targetMappingPath + map.TargetType.Id + "+", map.TargetType.Id);

            if (!map.SourceType.IsRecursive && !map.SourceType.IsStruct && !map.SourceType.IsPrimitive)
                map.SourceType.IsRecursive = map.AreSameType
                    ? map.TargetType.IsRecursive
                    : IsRecursive(sourceMappingPath + map.SourceType.Id + "+", map.SourceType.Id);
           
            return map;
        }

        if (IsCollection(ref map, out var targetCollInfo, out var sourceCollInfo, out var ltrInfo, out var rtlInfo))
        {
            int targetItemId = GetId(targetCollInfo.TypeSymbol),
                sourceItemId = GetId(sourceCollInfo.TypeSymbol);

            Member
                targetItem = new(target.Id, target.Name + "Item", targetCollInfo.IsItemNullable),
                sourceItem = new(source.Id, source.Name + "Item", sourceCollInfo.IsItemNullable);

            var itemMap = GetOrAddMapper(
                new(targetCollInfo.TypeSymbol.AsNonNullable()),
                new(sourceCollInfo.TypeSymbol.AsNonNullable()),
                targetItemId,
                sourceItemId);

            map.ItemMapId = itemMap.Id;

            map.TargetType.IsRecursive |= itemMap.TargetType.IsRecursive;
            map.SourceType.IsRecursive |= itemMap.SourceType.IsRecursive;

            if (targetItem.IsNullable)
                itemMap.AddTTSTryGet = true;

            if (sourceItem.IsNullable)
                itemMap.AddSTTTryGet = true;

            targetCollInfo.DataType = map.TargetType;
            sourceCollInfo.DataType = map.SourceType;
            targetCollInfo.ItemDataType = itemMap.TargetType;
            sourceCollInfo.ItemDataType = itemMap.SourceType;

            if (itemMap.CanMap is false)
            {
                map.CanMap = map.IsCollection = false;

                return map;
            }

            itemMap = CreateType(
                itemMap,
                sourceItem,
                targetItem,
                ApplyOn.None,
                sourceMappingPath,
                targetMappingPath);

            if (itemMap.CanMap is true && map.CanMap is null && true == (map.CanMap = 
                ignore is not ApplyOn.Target or ApplyOn.Both &&
                (!itemMap.TargetType.IsInterface && (map.RequiresSTTCall = BuildCollection(
                    target,
                    sourceItem,
                    targetItem,
                    map.RequiresSTTCall,
                    sourceCollInfo,
                    targetCollInfo,
                    rtlInfo,
                    itemMap.BuildToTargetValue,
                    ref map.BuildToTargetValue,
                    ref map.BuildToTargetMethod)))
                |
                (ignore is not ApplyOn.Source or ApplyOn.Both &&
                !itemMap.SourceType.IsInterface && (map.RequiresTTSCall = BuildCollection(
                    source,
                    targetItem,
                    sourceItem,
                    map.RequiresTTSCall,
                    targetCollInfo,
                    sourceCollInfo,
                    ltrInfo,
                    itemMap.BuildToSourceValue,
                    ref map.BuildToSourceValue,
                    ref map.BuildToSourceMethod))))
            )
            {
                map.AuxiliarMappings += itemMap.BuildMethods;
            }
            else
            {
                map.CanMap = false;
            }
        }
        else
        {
            var canMap = false;

            if (map.TargetType.HasConversionTo(map.SourceType, out var exists, out var isExplicit)
                | map.SourceType.HasConversionTo(map.TargetType, out var reverseExists, out var isReverseExplicit))
            {
                if (ignore is not ApplyOn.Target or ApplyOn.Both && exists)
                {
                    var scalar = isExplicit
                        ? $@"({map.TargetType.FullName}){{0}}"
                        : $@"{{0}}";

                    map.BuildToTargetValue = value => scalar.Replace("{0}", value);

                    canMap = map.HasToTargetScalarConversion = true;
                }

                if (ignore is not ApplyOn.Source or ApplyOn.Both && reverseExists)
                {
                    var scalar = isReverseExplicit
                        ? $@"({map.SourceType.FullName}){{0}}"
                        : $@"{{0}}";

                    map.BuildToSourceValue = value => scalar.Replace("{0}", value);

                    canMap = map.HasToTargetScalarConversion = true;
                }

                map.JustFill = canMap;
            }

            if (map.TargetType.IsPrimitive || map.SourceType.IsPrimitive)
            {
                map.CanMap = canMap;
                goto exit;
            }

            switch ((map.TargetType.IsTupleType, map.SourceType.IsTupleType))
            {
                case (true, true):
                    CreateTypeBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.TupleElements, map.TargetType.TupleElements);
                    break;
                case (true, false):
                    CreateTypeBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.Members, map.TargetType.TupleElements);
                    break;
                case (false, true):
                    CreateTypeBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.TupleElements, map.TargetType.Members);
                    break;
                default:
                    CreateTypeBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.Members, map.TargetType.Members);
                    break;
            }



        }
    exit:
        return map;
    }

    internal bool IsRecursive(string s, int id)
    {
        string n = $"+{id}+", ss;
        int t = 1;

        for (int nL = n.Length, start = Math.Abs(s.Length - n.Length), end = s.Length; start > -1 && end - start >= nL;)
        {
            if ((ss = s[start..end]) == n && t-- == 0)
            {
                    return true;
            }
            else if (ss[0] == '+' && ss[^1] == '+')
            {
                end = start + 1;
                start = end - nL;
            }
            else
            {
                end--;
                start--;
            }
        }
        return false;
    }

    private bool BuildCollection(
        Member target,
        Member sourceItem,
        Member targetItem,
        bool call,
        CollectionInfo sourceCollInfo,
        CollectionInfo targetCollInfo,
        CollectionMapping ltrInfo,
        ValueBuilder itemValueCreator,
        ref ValueBuilder valueCreator,
        ref Action? methodCreator)
    {
        string
            targetFullTypeName = targetCollInfo.DataType.ExportNotNullFullName,
            sourceFullTypeName = sourceCollInfo.DataType.ExportNotNullFullName,
            targetItemFullTypeName = targetCollInfo.ItemDataType.FullName,
            countProp = sourceCollInfo.CountProp,
            methodName = ltrInfo.ToTargetMethodName;

        bool IsRecursive(out int maxDepth)
        {
            var isRecursive = targetCollInfo.ItemDataType.IsRecursive;

            maxDepth = target.MaxDepth;

            if (targetCollInfo.ItemDataType.IsRecursive && target.MaxDepth == 0)
                maxDepth = target.MaxDepth = 1;

            return isRecursive;
        }

        string build()
        {
            string underlyingCollectionType = $"global::System.Collections.Generic.List<{targetItemFullTypeName}>()";

            (string defaultType, string initType, ValueBuilder returnExpr) = (targetCollInfo.Type, targetCollInfo.DataType.IsInterface) switch
            {
                (EnumerableType.ReadOnlyCollection, true) =>
                    ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyReadOnlyCollection", underlyingCollectionType, v => $"new global::System.Collections.ObjectModel.ReadOnlyCollection<{targetItemFullTypeName}>({v})"),
                (EnumerableType.Collection, true) =>
                    ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyCollection", underlyingCollectionType, v => v),
                _ =>
                    ("new " + targetFullTypeName + "()", targetFullTypeName + "()", new ValueBuilder(v => v))
            };

            //User? <== UserDto?
            var checkNull = (!targetItem.IsNullable || !targetCollInfo.ItemDataType.IsStruct) && sourceItem.IsNullable;

            string? suffix = (sourceCollInfo.Type, targetCollInfo.Type) is (not EnumerableType.Array, EnumerableType.ReadOnlySpan) ? ".AsSpan()" : null,
                sourceBang = sourceItem.Bang,
                defaultSourceBang = sourceItem.DefaultBang;

            string method = $@"
    /// <summary>
    /// Creates a new instance of <see cref=""{targetFullTypeName}""/> based from a given <see cref=""{sourceFullTypeName}""/>
    /// </summary>
    /// <param name=""input"">Data source to be mappped</param>{(targetCollInfo.ItemDataType.IsRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : null)}
    public static {targetFullTypeName} {methodName}(this {sourceFullTypeName} input{(targetCollInfo.ItemDataType.IsRecursive ? $@", int depth = 0, int maxDepth = {target.MaxDepth})
    {{
        if (depth >= maxDepth) 
            return {(ltrInfo.CreateArray
            ? $"global::System.Array.Empty<{targetItemFullTypeName}>()"
            : defaultType
            )};
" : @")
    {")}
        {(ltrInfo.CreateArray
            ? ltrInfo.Redim
                ? $@"int len = 0, aux = 16;
        var output = new {targetItemFullTypeName}[aux];"
                : $@"int len = input.{countProp};
        var output = new {targetItemFullTypeName}[len];"
            : $@"var output = new {initType};")}
";

            if (ltrInfo.Iterator == "for")
            {
                method += $@"
        for (int i = 0; i < len; i++)
        {{
            output[i] = {GenerateValue("input[i]", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)};
        }}

        return output{suffix};
    }}
";
            }
            else
            {
                method += $@"
        foreach (var item in input)
        {{";
                if (ltrInfo.CreateArray)
                {
                    method += $@"
            output[len{(ltrInfo.Redim ? null : "++")}] = {GenerateValue("item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)};";
                    method += ltrInfo.Redim
                        //redim array
                        ? $@"

            if (aux == ++len)
                global::System.Array.Resize(ref output, aux *= 2);
        }}
        
        return (len < aux ? output[..len] : output){suffix};
    }}"
                        //normal ending
                        : $@"
        }}

        return output{suffix};
    }}";

                }
                else
                {
                    method += $@"
            output.{ltrInfo.Method}({GenerateValue("item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)});
        }}

        return {returnExpr("output")};
    }}
";
                }
            }

            return method;
        }

        /*string buildFill()
        {
            string underlyingCollectionType = $"global::System.Collections.Generic.List<{targetItemFullTypeName}>()";

            (string defaultType, string initType, ValueBuilder returnExpr) = (targetCollInfo.Type, targetCollInfo.DataType.IsInterface) switch
            {
                (EnumerableType.ReadOnlyCollection, true) =>
                    ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyReadOnlyCollection", underlyingCollectionType, v => $"new global::System.Collections.ObjectModel.ReadOnlyCollection<{targetItemFullTypeName}>({v})"),
                (EnumerableType.Collection, true) =>
                    ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyCollection", underlyingCollectionType, v => v),
                _ =>
                    ("new " + targetFullTypeName + "()", targetFullTypeName + "()", new ValueBuilder(v => v))
            };

            var checkNull = sourceItem.IsNullable;

            string? suffix = (sourceCollInfo.Type, targetCollInfo.Type) is (not EnumerableType.Array, EnumerableType.ReadOnlySpan) ? ".AsSpan()" : null,
                sourceBang = sourceItem.Bang,
                defaultSourceBang = sourceItem.DefaultBang;

            string method = $@"
    /// <summary>
    /// Creates a new instance of <see cref=""{targetFullTypeName}""/> based from a given <see cref=""{sourceFullTypeName}""/>
    /// </summary>
    /// <param name=""input"">Data source to be mappped</param>{(target.MaxDepth > 0 ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : null)}
    public static void FillFrom{sourceMethodName}(this {targetFullTypeName} output, {sourceFullTypeName} input{(target.MaxDepth > 0 ? $@", int depth = 0, int maxDepth = {target.MaxDepth})
    {{
        if (depth >= maxDepth)
        {{
            {targetCollInfo.Type switch
            {
                EnumerableType.Queue or EnumerableType.Stack or EnumerableType.Collection => "output.Clear();",

            }};
            return;
        }}
" : @")
    {")}
        {(ltrInfo.CreateArray
            ? ltrInfo.Redim
                ? $@"int len = 0, aux = 16;        global::System.Array.Resize(ref output, aux);"
                : $@"int len = input.{countProp};
        global::System.Array.Resize(ref output, len);"
            : $@"output.Clear();")}
";
            if (ltrInfo.Iterator == "for")
            {
                method += $@"
        for (int i = 0; i < len; i++)
        {{
            output[i] = {GenerateValue("input[i]", itemValueCreator, checkNull, sourceBang, defaultSourceBang)};
        }}
    }}";
            }
            else
            {
                method += $@"
        foreach (var item in input)
        {{";
                if (ltrInfo.CreateArray)
                {
                    method += $@"
            output[len{(ltrInfo.Redim ? null : "++")}] = {GenerateValue("item", itemValueCreator, checkNull, sourceBang, defaultSourceBang)};";
                    method += ltrInfo.Redim
                        //redim array
                        ? $@"

            if (aux == ++len)
                global::System.Array.Resize(ref output, aux *= 2);
        }}
        
        if (len < aux) 
            output = output[..len]{suffix};
    }}"
                        //normal ending
                        : $@"
        }}

        return output{suffix};
    }}";

                }
                else
                {
                    method += $@"
            output.{ltrInfo.Method}({GenerateValue("item", itemValueCreator, checkNull, sourceBang, defaultSourceBang)});
        }}

        return {returnExpr("output")};
    }}";
                }
            }

            return method;
        }*/

        var called = false;

        valueCreator = value => methodName + "(" + value + (IsRecursive(out var maxDepth) ? ", -1 + depth + 1" + (maxDepth > 1 ? ", " + maxDepth : null) : null) + ")";
        methodCreator = () => { if (!called) { called = true; code.Append(build()); } };

        return true;
    }

    static string GenerateValue
    (
        string item,
        ValueBuilder generateValue,
        bool checkNull,
        bool call,
        bool isValueType,
        string? sourceBang,
        string? defaultSourceBang
    )
    {;
        var indexerBracketPos = item.IndexOf('[');
        bool hasIndexer = indexerBracketPos > -1,
            shouldCache = checkNull && call && (hasIndexer || item.Contains('.'));
        var itemCache = shouldCache
            ? (hasIndexer ? "_" + item[..indexerBracketPos] : item).Replace(".", "")
            : item;

        return checkNull
            ? call
              ? $"{item} is {{}} {itemCache} ? {generateValue(itemCache)} : default{defaultSourceBang}"
              : isValueType && defaultSourceBang != null 
                ? buildNullCoalesceDefault(generateValue(itemCache)+" ?? default!")
                : generateValue(itemCache) + sourceBang
            : generateValue(item) + sourceBang;

        string buildNullCoalesceDefault(string value)
        {
            return value[0] == '('
                ? value.Replace(item, "(" + item) + ")"
                : value;
        }

        //return (generateValue != null, indexPos > -1, isValueType, call, checkNull, shouldCache) switch
        //{
        //    (true, true, _, true, true, true)
        //        => "{0} is {{}} {1} ? {2} : {3}"
        //            .Replace("{0}", item)
        //            .Replace("{1}", itemCache = "_" + item[..indexPos])
        //            .Replace("{2}", generateValue!(itemCache))
        //            .Replace("{3}", "default" + defaultSourceBang),
        //    (true, _, _, true, false, true)
        //        => "{0} is {{}} {1} ? {2} : {3}"
        //            .Replace("{0}", item)
        //            .Replace("{1}", itemCache = "_" + item.Replace(".", ""))
        //            .Replace("{2}", generateValue!(itemCache))
        //            .Replace("{3}", "default" + defaultSourceBang),
        //    //(false, true) => generateValue!(itemCache = "_" + (indexPos > -1 ? item[..item.IndexOf("[")] : item).Replace(".", "")),
        //    //(false, false) => generateValue!(item),
        //    //(true, true) => itemCache = "_" + (indexPos > -1 ? item[..item.IndexOf("[")] : item).Replace(".", ""),
        //    _ => item //
        //};

        //return checkNull
        //    ?// call  ?
        //    (!call && isValueType && defaultSourceBang != null
        //        ? $"{item} ?? default{defaultSourceBang}"
        //        : $"{item} is {{}}{(shouldCache ? " " + itemCache : null)} ? {value} : default{defaultSourceBang}")
        //    //: isValueType && defaultSourceBang != null
        //            //? $"{item} ?? default{defaultSourceBang}"
        //            //: value + sourceBang
        //    : value + sourceBang;
    }

    internal static int GetId(ITypeSymbol type)
    {
        return _comparer.GetHashCode(type.Name == "Nullable"
            ? ((INamedTypeSymbol)type).TypeArguments[0]
            : type);
    }

    private void CreateTypeBuilders<TTarget, TSource>
    (
        TypeMappingInfo map,
        ApplyOn ignore,
        string sourceMappingPath,
        string targetMappingPath,
        Member source,
        Member target,
        IEnumerable<TSource> sourceMembers,
        IEnumerable<TTarget> targetMembers
    )
    where TTarget : ISymbol
    where TSource : ISymbol
    {
        if (map is { IsCollection: not true, TargetType.IsInterface: true, SourceType.IsInterface: true })
            return;

        string
            sourceExportFullTypeName = map.SourceType.ExportNotNullFullName,
            targetExportFullTypeName = map.TargetType.ExportNotNullFullName,
            ttsTypeStart = map.SourceType.IsTupleType ? TUPLE_START : string.Format(TYPE_START, map.SourceType.NotNullFullName),
            ttsTypeEnd = map.SourceType.IsTupleType ? TUPLE_END : TYPE_END,
            sttTypeStart = map.TargetType.IsTupleType ? TUPLE_START : string.Format(TYPE_START, map.TargetType.NotNullFullName),
            sttTypeEnd = map.TargetType.IsTupleType ? TUPLE_END : TYPE_END,
            ttsSpacing = map.SourceType.IsTupleType ? " " : @"
            ",
            sttSpacing = map.TargetType.IsTupleType ? " " : @"
            ";

        MemberBuilder
            ttsMembers = null!,
            sttMembers = null!;

        string?
            sttComma = null,
            ttsComma = null;

        uint mapId = map.Id;

        bool
            toSameType = map.AreSameType,
            hasComplexTTSMembers = false,
            hasComplexSTTMembers = false,
            isSTTRecursive = false,
            isTTSRecursive = false,
            parentIgnoreTarget = ignore is ApplyOn.Target or ApplyOn.Both,
            parentIgnoreSource = ignore is ApplyOn.Source or ApplyOn.Both;
        
        if(map.BuildToTargetValue is null || !map.TargetType.IsValueType)
        {
            map.RequiresSTTCall = true;
            map.BuildToTargetValue = val => map.ToTargetMethodName + "(" + val + (isSTTRecursive ? ", depth + 1" + (source.MaxDepth > 1 ? ", " + source.MaxDepth : null) : null) + ")";
        }

        map.BuildToTargetMethod = () =>
        {
            if (map.IsSTTRendered)
                return;

            var maxDepth = target.MaxDepth;

            if (isSTTRecursive && target.MaxDepth == 0)
                maxDepth = target.MaxDepth = 1;

            map.RenderedSTTDefaultMethod = true;

            CreateDefaultMethod(isSTTRecursive, maxDepth, toSameType, sttTypeStart, sttTypeEnd, map.ToTargetMethodName, sourceExportFullTypeName, targetExportFullTypeName, source.DefaultBang, sttMembers, hasComplexSTTMembers);
            
            if (MappingKind.Fill.HasFlag(map.MappingsKind))
            {
                map.RenderedSTTFillMethod = true;

                CreateFillMethod(isSTTRecursive, maxDepth, map.TargetType.IsValueType, map.FillTargetFromSourceMethodName, targetExportFullTypeName, sourceExportFullTypeName, source.DefaultBang, sttMembers, hasComplexSTTMembers);

                foreach (var item in map.TargetType.UnsafePropertyFieldsGetters)
                    item.Render(code);
            }

            if (map.AddSTTTryGet)
            {
                map.RenderedSTTTryGetMethod = true;

                CreateTryGetMethod(isSTTRecursive, maxDepth, toSameType, map.TryGetTargetMethodName, map.ToTargetMethodName, sourceExportFullTypeName, targetExportFullTypeName, source.DefaultBang, sttMembers, hasComplexSTTMembers);
            }
        };

        if (map.BuildToSourceValue is null || !map.SourceType.IsValueType)
        {
            map.RequiresTTSCall = true;
            map.BuildToSourceValue = val => map.ToSourceMethodName + "(" + val + (isTTSRecursive ? ", depth + 1" + (target.MaxDepth > 1 ? ", " + target.MaxDepth : null) : null) + ")";
        }

        map.BuildToSourceMethod = () =>
        {
            if (map.IsTTSRendered)
                return;

            if (isTTSRecursive && source.MaxDepth == 0)
                source.MaxDepth = 1;

            map.RenderedTTSDefaultMethod = true;

            CreateDefaultMethod(isTTSRecursive, source.MaxDepth, toSameType, ttsTypeStart, ttsTypeEnd, map.ToSourceMethodName, targetExportFullTypeName, sourceExportFullTypeName, target.DefaultBang, ttsMembers, hasComplexTTSMembers);

            if (MappingKind.Fill.HasFlag(map.MappingsKind))
            {
                map.RenderedTTSFillMethod = true;

                CreateFillMethod(isTTSRecursive, source.MaxDepth, map.SourceType.IsValueType, map.FillSourceFromTargetMethodName, sourceExportFullTypeName, targetExportFullTypeName, target.DefaultBang, ttsMembers, hasComplexTTSMembers);

                foreach (var item in map.SourceType.UnsafePropertyFieldsGetters)
                    item.Render(code);
            }

            if(map.AddTTSTryGet)
            {
                map.RenderedTTSTryGetMethod = true;

                CreateTryGetMethod(isTTSRecursive, source.MaxDepth, toSameType, map.TryGetSourceMethodName, map.ToSourceMethodName, targetExportFullTypeName, sourceExportFullTypeName, target.DefaultBang, ttsMembers, hasComplexTTSMembers);
            }

            map.BuildToSourceMethod = null;
        };


        var allowLowerCase = map.TargetType.IsTupleType || map.SourceType.IsTupleType;

        foreach(var targetItem in targetMembers)
        {
            if (IsNotMappable(targetItem, out var targetMemberType, out var targetTypeId, out var targetMember))
                continue;

            foreach (var sourceItem in sourceMembers)
            {
                if (IsNotMappable(sourceItem, out var sourceMemberType, out var sourceTypeId, out var sourceMember)
                    || AreNotMappableByDesign(allowLowerCase, sourceMember, targetMember, out var ignoreSource, out var ignoreTarget))

                    continue;

                if (mapId == GetId(sourceTypeId, targetTypeId))
                {
                    targetMember.Type = targetMember.OwningType = map.TargetType;
                    sourceMember.Type = sourceMember.OwningType = map.SourceType;

                    if (!(parentIgnoreTarget || ignoreTarget || map.TargetType.IsInterface))
                    {
                        map.STTMemberCount++;

                        if (targetMember.IsNullable)
                            map.AddSTTTryGet = true;

                        map.TargetType.IsRecursive = isSTTRecursive = true;

                        hasComplexSTTMembers = true;

                        sttMembers += isFill =>
                        {
                            code.Append(BuildTypeMember(
                                isFill,
                                ref sttComma,
                                sttSpacing,
                                map.BuildToTargetValue,
                                sourceMember,
                                targetMember,
                                map.ToTargetMethodName,
                                map.FillTargetFromSourceMethodName,
                                map.RequiresSTTCall));

                            if(targetMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += () => nullUnsafeAccesor.Render(code);
                        };
                    }

                    if (!(toSameType && parentIgnoreSource || ignoreSource || map.SourceType.IsInterface))
                    {
                        map.TTSMemberCount++;

                        map.SourceType.IsRecursive = isTTSRecursive = true;
                        hasComplexTTSMembers = true;

                        if (sourceMember.IsNullable)
                            map.AddTTSTryGet = true;

                        ttsMembers += isFill =>
                        {
                            code.Append(BuildTypeMember(
                                isFill,
                                ref ttsComma,
                                ttsSpacing,
                                map.BuildToSourceValue,
                                targetMember,
                                sourceMember,
                                map.ToSourceMethodName,
                                map.FillSourceFromTargetMethodName,
                                map.RequiresTTSCall));

                            if (sourceMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += () => nullUnsafeAccesor.Render(code);
                        };
                    }

                    map.CanMap |= map.HasTargetToSourceMap || map.HasTargetToSourceMap;

                    break;
                }

                var memberMap = GetOrAddMapper(
                    new(targetMemberType),
                    new(sourceMemberType),
                    targetTypeId,
                    sourceTypeId);

                memberMap = CreateType(
                    memberMap,
                    sourceMember,
                    targetMember,
                    ApplyOn.None,
                    sourceMappingPath,
                    targetMappingPath);

                if (memberMap is { CanMap: not false })
                {
                    targetMember.OwningType = map.TargetType;
                    sourceMember.OwningType = map.SourceType;

                    if (!(parentIgnoreTarget || ignoreTarget || map.TargetType.IsInterface))
                    {
                        map.STTMemberCount++;

                        if (sourceMember.IsNullable)
                            memberMap.AddSTTTryGet = true;

                        hasComplexSTTMembers = !memberMap.TargetType.IsPrimitive;

                        map.TargetType.IsRecursive |= 
                            isSTTRecursive |= 
                            memberMap.TargetType.IsRecursive |= 
                            memberMap.IsCollection is true && memberMap.TargetType.CollectionInfo.ItemDataType.Id == target.Type.Id;

                        sttMembers += isFill =>
                        {
                            code.Append(BuildTypeMember(
                                isFill,
                                ref sttComma,
                                sttSpacing,
                                memberMap.BuildToTargetValue,
                                sourceMember,
                                targetMember,
                                memberMap.ToTargetMethodName,
                                memberMap.FillTargetFromSourceMethodName,
                                memberMap.RequiresSTTCall));

                            if (targetMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += () => nullUnsafeAccesor.Render(code);
                        };
                    }

                    if (!(toSameType || parentIgnoreSource || ignoreSource || map.SourceType.IsInterface))
                    {
                        map.TTSMemberCount++;

                        if (targetMember.IsNullable)
                            memberMap.AddTTSTryGet = true;

                        hasComplexTTSMembers = !memberMap.SourceType.IsPrimitive;

                        map.SourceType.IsRecursive |= 
                            isTTSRecursive |= 
                            memberMap.SourceType.IsRecursive |=
                            memberMap.IsCollection is true && memberMap.SourceType.CollectionInfo.ItemDataType.Id == source.Type.Id;

                        ttsMembers += isFill =>
                        {
                            code.Append(BuildTypeMember(
                                isFill,
                                ref ttsComma,
                                ttsSpacing,
                                memberMap.BuildToSourceValue,
                                targetMember,
                                sourceMember,
                                memberMap.ToSourceMethodName,
                                memberMap.FillSourceFromTargetMethodName,
                                memberMap.RequiresTTSCall));

                            if (sourceMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += () => nullUnsafeAccesor.Render(code);
                        };
                    }

                    if (map.MappingsKind == MappingKind.All && memberMap.MappingsKind != MappingKind.All 
                        && (sourceMember.IsInit || targetMember.IsInit))
                        memberMap.MappingsKind = MappingKind.All;

                    if (true == (map.CanMap |= map.HasTargetToSourceMap || map.HasSourceToTargetMap)
                        && (!memberMap.TargetType.IsPrimitive || memberMap.TargetType.IsTupleType
                        || !memberMap.SourceType.IsPrimitive || memberMap.SourceType.IsTupleType))
                        map.AuxiliarMappings += memberMap.BuildMethods;

                    break;
                }
            }
        }

        if (!map.HasSourceToTargetMap)        
        {
            map.AddSTTTryGet = false;
        }

        if (!map.HasTargetToSourceMap)
        {
            map.AddTTSTryGet = false;
        }

        void CreateDefaultMethod(
            bool isRecursive,
            int maxDepth,
            bool toSameType,
            string typeStart,
            string typeEnd,
            string methodName,
            string sourceFullTypeName,
            string targetFullTypeName,
            string? defaultSourceBang,
            MemberBuilder members,
            bool hasComplexMembers)
        {
            if (members is null) return;
            code.Append($@"
    /// <summary>
    /// Creates a new instance of <see cref=""{targetExportFullTypeName}""/> based from a given instance of <see cref=""{sourceExportFullTypeName}""/>
    /// </summary>
    /// <param name=""input"">Data source to be mappped</param>{(isRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : null)}
    public static {targetFullTypeName} {methodName}(this {sourceFullTypeName} input{(
                isRecursive
                    ? $@", int depth = 0, int maxDepth = {maxDepth})
    {{
        if (depth >= maxDepth) 
            return default{defaultSourceBang};
"
                    : $@")
    {{")}
        return {typeStart}");

            members(false);

            code.Append($@"{typeEnd};
    }}
");
        }

        void CreateTryGetMethod
        (
            bool isRecursive,
            short maxDepth,
            bool toSameType,
            string methodName,
            string toMethodName,
            string sourceFullTypeName,
            string targetFullTypeName,
            string? defaultSourceBang,
            MemberBuilder members,
            bool hasComplexMembers
        )
        {
            if (members is null) return;
            code.Append($@"
    /// <summary>
    /// Tries to create a new instance of <see cref=""{targetFullTypeName}""/> based from a given instance of <see cref=""{sourceFullTypeName}""/> if it's not null
    /// </summary>
    /// <param name=""input"">Data source</param>{(isRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : null)}
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool {methodName}(this {sourceFullTypeName} input, out {targetFullTypeName} output{(
                isRecursive
                    ? $@", int depth = 0, int maxDepth = {maxDepth})
    {{"
                    : $@")
    {{")}
        if (input is {{}} _input)
        {{
            output = {toMethodName}(_input{(isRecursive ? ", depth, maxDepth" : null)});
            return true;
        }}
        output = default{defaultSourceBang};
        return false;
    }}
");
        }

        void CreateFillMethod
        (
            bool isRecursive,
            short maxDepth,
            bool isValueType,
            string fillMethodName,
            string targetFullTypeName,
            string sourceFullTypeName,
            string? sourceBang,
            MemberBuilder members,
            bool hasComplexMembers
        )
        {
            if (members is null) return;
            string? _ref = (isValueType ? "ref " : null);
            code.Append($@"
    /// <summary>
    /// Update an instance of <see cref=""{targetFullTypeName}""/> based from a given instance of <see cref=""{sourceFullTypeName}""/> 
    /// </summary>
    /// <param name=""input"">Data source to be mappped</param>{(isRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : null)}
    public static {_ref}{targetFullTypeName} {fillMethodName}({_ref}this {targetFullTypeName} output, {sourceFullTypeName} input{(
                isRecursive
                    ? $@", int depth = 0, int maxDepth = {maxDepth})
    {{
        if (depth >= maxDepth) 
            return {_ref}output{sourceBang};
"
                    : $@")
    {{")}");

            members(true);

            code.Append($@"

        return {_ref}output;
    }}
");
        }
    }

    delegate void MemberBuilder(bool isFill);



    bool IsCollection
    (
        ref TypeMappingInfo map,
        out CollectionInfo targetColl,
        out CollectionInfo sourceColl,
        out CollectionMapping ltrInfo,
        out CollectionMapping rtlInfo
    )
    {
        if (map.IsCollection is false)
        {
            SetDefaults(out targetColl, out sourceColl, out ltrInfo, out rtlInfo);
            return false;
        }

        if (map.IsCollection is true)
        {
            targetColl = map.TargetType.CollectionInfo;
            sourceColl = map.SourceType.CollectionInfo;
            ltrInfo = map.TTSCollection;
            rtlInfo = map.STTCollection;
            return true;
        }
        else
        {
            SetDefaults(out targetColl, out sourceColl, out ltrInfo, out rtlInfo);

            if (map.TargetType.IsIterable is false || map.SourceType.IsIterable is false)
                return false;


            if (map.TargetType.IsIterable is true)
            {
                targetColl = map.TargetType.CollectionInfo;
            }
            else if (map.TargetType.IsIterable is null
                && true == (map.TargetType.IsIterable = IsEnumerableType(map.TargetType.NonGenericFullName, map.TargetType._typeSymbol, out targetColl)))
            {
                map.TargetType.CollectionInfo = targetColl;
            }
            else
                return false;

            if (map.SourceType.IsIterable is true)
            {
                sourceColl = map.SourceType.CollectionInfo;
            }
            else if (map.SourceType.IsIterable is null
                && true == (map.SourceType.IsIterable = IsEnumerableType(map.SourceType.NonGenericFullName, map.SourceType._typeSymbol, out sourceColl)))
            {
                map.SourceType.CollectionInfo = sourceColl;
            }
            else
                return false;

            if (map.IsCollection is null)
            {
                ltrInfo = map.TTSCollection = GetResult(targetColl, sourceColl, map.ToSourceMethodName);
                rtlInfo = map.STTCollection = GetResult(sourceColl, targetColl, map.ToTargetMethodName);
                map.IsCollection = true;
                return true;
            }

            map.IsCollection = false;

            return false;
        }

        static void SetDefaults(out CollectionInfo targetColl, out CollectionInfo sourceColl, out CollectionMapping ltrInfo, out CollectionMapping rtlInfo)
        {
            ltrInfo = default;
            rtlInfo = default;
            targetColl = default!;
            sourceColl = default!;
        }
    }

    private static CollectionInfo GetCollectionInfo(EnumerableType enumerableType, ITypeSymbol typeSymbol)
        => enumerableType switch
        {
#pragma warning disable format
            EnumerableType.Queue =>
                new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), false, true, true,   false,  "Enqueue",  "Count"),
            EnumerableType.Stack =>
                new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), false, true, true,   false,  "Push",     "Count"),
            EnumerableType.Enumerable =>
                new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), false, true, false,  true,   null,       "Length"),
            EnumerableType.ReadOnlyCollection =>
                new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, true,  true,   false,  "Add",      "Count"),
            EnumerableType.ReadOnlySpan =>
                new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, true,  true,   true,   null,       "Length"),
            EnumerableType.Collection =>
                new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, false, true,   false,  "Add",      "Count"),
            EnumerableType.Span =>
                new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, false, true,   true,   null,       "Length"),
            _ =>
                new(typeSymbol.AsNonNullable(), enumerableType, typeSymbol.IsNullable(), true, false, true,   true,   null,       "Length")
#pragma warning restore format
        };

    static CollectionMapping GetResult(CollectionInfo a, CollectionInfo b, string toMethodName)
    {
        var redim = !a.Countable && b.BackingArray;

        var iterator = a.Indexable && b.BackingArray ? "for" : "foreach";

        return new(b.BackingArray, b.BackingArray && !a.Indexable, iterator, redim, b.Method, toMethodName);
    }

    bool IsEnumerableType(string fullNonGenericName, ITypeSymbol type, out CollectionInfo info)
    {
        if (type.IsPrimitive(true))
        {
            info = default!;
            return false;
        }

        switch (fullNonGenericName)
        {
            case "global::System.Collections.Generic.Stack"
            :
                info = GetCollectionInfo(EnumerableType.Stack, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                info = GetCollectionInfo(EnumerableType.Queue, GetEnumerableType(compilation, type));

                return true;

            case "global::System.ReadOnlySpan"
            :

                info = GetCollectionInfo(EnumerableType.ReadOnlySpan, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Span"
            :
                info = GetCollectionInfo(EnumerableType.Span, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                info = GetCollectionInfo(EnumerableType.Collection, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                info = GetCollectionInfo(EnumerableType.ReadOnlyCollection, GetEnumerableType(compilation, type));

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                info = GetCollectionInfo(EnumerableType.Enumerable, GetEnumerableType(compilation, type));

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

        static ITypeSymbol GetEnumerableType(Compilation compilation, ITypeSymbol enumerableType)
        {
            return ((INamedTypeSymbol)enumerableType).TypeArguments.FirstOrDefault() ?? compilation.ObjectType;
        }
    }

    static bool IsNotMappable(ISymbol member, out ITypeSymbol typeOut, out int typeIdOut, out Member memberOut)
    {
        var isAccesible = member.DeclaredAccessibility is Accessibility.Internal or Accessibility.Public or Accessibility.ProtectedAndInternal or Accessibility.ProtectedOrInternal;

        switch (member)
        {
            case IPropertySymbol
            {
                ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                IsIndexer: false,
                Type: { } type,
                IsReadOnly: var isReadonly,
                IsWriteOnly: var isWriteOnly,
                SetMethod.IsInitOnly: var isInitOnly
            }:
                (typeOut, typeIdOut, memberOut) = (
                    type.AsNonNullable(),
                    GetId(type),
                    new(_comparer.GetHashCode(member),
                        member.ToNameOnly(),
                        type.IsNullable(),
                        isAccesible && !isWriteOnly,
                        isAccesible && !isReadonly,
                        member.GetAttributes(),
                        isInitOnly == true,
                        isAccesible,
                        true));

                return false;

            case IFieldSymbol
            {
                ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                Type: { } type,
                AssociatedSymbol: null,
                IsReadOnly: var isReadonly
            }:
                (typeOut, typeIdOut, memberOut) =
                    (type.AsNonNullable(),
                     GetId(type),
                     new(_comparer.GetHashCode(member),
                        member.ToNameOnly(),
                        type.IsNullable(),
                        isAccesible,
                        isAccesible && !isReadonly,
                        member.GetAttributes(),
                        isAccesible
                    ));

                return false;

            default:
                (typeOut, typeIdOut, memberOut) = (default!, default, default!);

                return true;
        }
    }

    string? BuildTypeMember(
        bool isFill,
        ref string? comma,
        string spacing,
        ValueBuilder createType,
        Member source,
        Member target,
        string sourceToTargetMethodName,
        string fillTargetFromMethodName,
        bool call)
    {
        bool checkNull = (!target.IsNullable || !target.Type.IsPrimitive) && source.IsNullable;

        var _ref = target.Type.IsValueType ? "ref " : null;

        if (isFill)
        {
            string
                sourceMember = string.Intern("input." + source.Name),
                targetMember = string.Intern("output." + target.Name),
                targetType = target.Type.NotNullFullName,
                parentFullName = target.OwningType!.NotNullFullName,
                targetBang = target.Bang!,
                targetDefaultBang = target.DefaultBang!,
                getPrivFieldMethodName = $"Get{target.OwningType!.Sanitized}_{target.Name}_PF",
                getNullMethodName = $"GetValueOfNullableOf{target.Type.Sanitized}"; 

            var declareRef = !target.IsWritable || target.IsInit || target.Type.IsValueType;

            if (target.Type.IsMultiMember && declareRef)
            {
                if (!canUseUnsafeAccessor)
                    return null;

                if (target.IsNullable)
                {
                    target.Type.NullableMethodUnsafeAccessor ??= new($@"
    /// <summary>
    /// Gets a reference to the not nullable value of <see cref=""global::System.Nullable{{{targetType}}}""/>
    /// </summary>
    /// <param name=""_"">Target nullable type</param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ""value"")]
    extern static ref {targetType} {getNullMethodName}(ref {targetType}? _);
");
                }

                target.OwningType!.UnsafePropertyFieldsGetters.Add(new(targetMember, $@"
    /// <summary>
    /// Gets a reference to {(target.IsProperty ? $"the backing field of {parentFullName}.{target.Name} property" : $"the field {parentFullName}.{target.Name}")}
    /// </summary>
    /// <param name=""_""><see cref=""global::System.Nullable{{{targetType}}}""/> container reference of {target.Name} {(target.IsProperty ? "property" : "field")}</param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ""{(target.IsProperty ? $"<{target.Name}>k__BackingField" : target.Name)}"")]
    extern static ref {targetType}{(target.IsNullable ? "?" : null)} {getPrivFieldMethodName}({parentFullName} _);
"));
            }

            if (!target.Type.IsMultiMember && (!target.IsWritable || target.OwningType?.IsReadOnly is true || (target.OwningType is { IsTupleType: false } && target.IsInit)))
                return null;

            string
                outputVal = "_output" + target.Name,
                inputVal = "_input" + target.Name,
                fillFirstParam = declareRef 
                    ? target.IsNullable
                        ? $"{_ref}{getNullMethodName}(ref {outputVal})" 
                        : $"{_ref}{outputVal}"
                    : targetMember;

            var start = declareRef 
                ? $@"
        // Nullable hack to fill without changing reference
        ref var {outputVal} = ref {getPrivFieldMethodName}(output);" 
                : null;

            if (target.Type.IsMultiMember) // Usar el metodo Fill en vez de asignar
            {
                if (target.IsNullable)
                {
                    var notNullRightSideCheck = target.Type.IsStruct ? ".HasValue" : " is not null";
                    if (source.IsNullable)
                    {
                        return $@"{start}
        if ({sourceMember} is {{}} {inputVal})
            if({targetMember}{notNullRightSideCheck})
                {fillTargetFromMethodName}({fillFirstParam}, {inputVal});{(declareRef ? $@"
            else
                {outputVal} = {sourceToTargetMethodName}({inputVal});
        else
            {outputVal} = default{targetDefaultBang};" : $@"
            else
                {targetMember} = {sourceToTargetMethodName}({inputVal});
        else
            {targetMember} = default{targetDefaultBang};")}
";
                    }

                    return $@"{start}
        if({outputVal}{notNullRightSideCheck})
            {fillTargetFromMethodName}({fillFirstParam}, {sourceMember});{(declareRef ? $@"
        else
            {outputVal} = {sourceToTargetMethodName}({sourceMember});" : $@"
        else
            {targetMember} = {sourceToTargetMethodName}({sourceMember});")}";

                }
                else if (source.IsNullable)
                {

                    return $@"{start}
        if({sourceMember} is {{}} _in)
            {fillTargetFromMethodName}({fillFirstParam}, _in);{(declareRef ? $@"
        else
            {outputVal} = default{targetDefaultBang};" : $@"
        else
            {targetMember} = default{targetDefaultBang};")}
";
                }

                return $@"{start}
        {fillTargetFromMethodName}({fillFirstParam}, {sourceMember});
";
            }

            return $@"
        {targetMember} = {GenerateValue(sourceMember, createType, checkNull, call, target.Type.IsValueType, source.Bang, source.DefaultBang)};";

        }
        else
        {
            return Exch(ref comma, ",") + spacing + (target.OwningType?.IsTupleType is not true ? target.Name + " = " : null)
                + GenerateValue("input." + source.Name, createType, checkNull, call, target.Type.IsValueType, source.Bang, source.DefaultBang);
        }
    }

    bool AreNotMappableByDesign(bool ignoreCase, Member source, Member target, out bool ignoreSource, out bool ignoreTarget)
    {
        ignoreSource = ignoreTarget = false;

        return !(target.Name.Equals(
            source.Name, 
            ignoreCase 
                ? StringComparison.InvariantCultureIgnoreCase 
                : StringComparison.InvariantCulture)
            | CheckMappability(target, source, ref ignoreSource, ref ignoreTarget)
            | CheckMappability(source, target, ref ignoreTarget, ref ignoreSource));
    }

    bool CheckMappability(Member target, Member source, ref bool ignoreTarget, ref bool ignoreSource)
    {
        if (!target.IsWritable || !source.IsReadable)
            return false;

        bool canWrite = false;

        foreach (var attr in target.Attributes)
        {
            if (attr.AttributeClass?.ToGlobalNamespace() is not { } className) continue;

            if (className == "global::SourceCrafter.Bindings.Attributes.IgnoreBindAttribute")
            {
                switch ((ApplyOn)(int)attr.ConstructorArguments[0].Value!)
                {
                    case ApplyOn.Target:
                        ignoreTarget = true;
                        return false;
                    case ApplyOn.Both:
                        ignoreTarget = ignoreSource = true;
                        return false;
                    case ApplyOn.Source:
                        ignoreSource = true;
                        break;
                }

                if (ignoreTarget && ignoreSource)
                    return false;

                continue;
            }

            if (className == "global::SourceCrafter.Bindings.Attributes.MaxAttribute")
            {
                target.MaxDepth = (short)attr.ConstructorArguments[0].Value!;
                continue;
            }

            if (className != "global::SourceCrafter.Bindings.Attributes.BindAttribute")
                continue;

            if ((attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0].Expression
                 is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                }
                 && _comparer.GetHashCode(compilation.GetSemanticModel(id.SyntaxTree).GetSymbolInfo(id).Symbol) == source.Id)
            {
                canWrite = true;

                switch ((ApplyOn)(int)attr.ConstructorArguments[1].Value!)
                {
                    case ApplyOn.Target:
                        ignoreTarget = true;
                        return false;
                    case ApplyOn.Both:
                        ignoreTarget = ignoreSource = true;
                        return false;
                    case ApplyOn.Source:
                        ignoreSource = true;
                        break;
                }

                if (ignoreTarget && ignoreSource)
                    return false;
            }
        }
        return canWrite;
    }

    static void GetNullability(Member target, ITypeSymbol targetType, Member source, ITypeSymbol sourceType)
    {
        source.DefaultBang = GetDefaultBangChar(target.IsNullable, source.IsNullable, sourceType.AllowsNull());
        source.Bang = GetBangChar(target.IsNullable, source.IsNullable);
        target.DefaultBang = GetDefaultBangChar(source.IsNullable, target.IsNullable, targetType.AllowsNull());
        target.Bang = GetBangChar(source.IsNullable, target.IsNullable);
    }

    static uint GetId(int targetId, int sourceId)
        => (uint)(Math.Min(targetId, sourceId), Math.Max(targetId, sourceId)).GetHashCode();

    static string? GetDefaultBangChar(bool isTargetNullable, bool isSourceNullable, bool sourceAllowsNull)
        => !isTargetNullable && (sourceAllowsNull || isSourceNullable) ? "!" : null;

    static string? GetBangChar(bool isTargetNullable, bool isSourceNullable)
        => !isTargetNullable && isSourceNullable ? "!" : null;

    static string? Exch(ref string? init, string? update = null) => ((init, update) = (update, init)).update;

    TypeMappingInfo GetOrAddMapper(TypeMapInfo target, TypeMapInfo source, int targetId, int sourceId)
    {
        var entries = _entries;

        var hashCode = GetId(targetId, sourceId);

        uint collisionCount = 0;
        ref int bucket = ref GetBucket(hashCode);
        int i = bucket - 1; // Value in _buckets is 1-based

        ref var entry = ref Unsafe.NullRef<TypeMapping>();

        TypeData? targetTD = null, sourceTD = null;

        while (true)
        {
            // Should be a while loop https://github.com/dotnet/runtime/issues/9422
            // Test uint in if rather than loop condition to drop range check for following array access
            if ((uint)i >= (uint)entries.Length)
            {
                break;
            }

            if (_uintComparer.Equals((entry = ref entries[i]).Id, hashCode))
            {
                return entry.Info;
            }

            entry.Info.GatherTarget(ref targetTD, targetId);
            entry.Info.GatherSource(ref sourceTD, sourceId);

            i = entry.next;

            collisionCount++;

            if (collisionCount > (uint)entries.Length)
            {
                // The chain of entries forms a loop; which means a concurrent update has happened.
                // Break out of the loop and throw, rather than looping forever.
                throw new NotSupportedException("Concurrent operations are not allowed");
            }


        }

        int index;
        if (_freeCount > 0)
        {
            index = _freeList;
            Debug.Assert((-3 - entries[_freeList].next) >= -1, "shouldn't overflow because `next` cannot underflow");
            _freeList = -3 - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            if (_count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket(hashCode);
            }
            index = (int)_count;
            _count++;
            entries = _entries;
        }

        entries[index] = new(
            hashCode,
            bucket - 1,
            new(hashCode,
                targetTD ??= typeSet.GetOrAdd(target, targetId),
                sourceTD ??= typeSet.GetOrAdd(source, sourceId)));

        entry = ref entries[index];

        entry.next = bucket - 1; // Value in _buckets is 1-based

        bucket = index + 1; // Value in _buckets is 1-based

        _version++;

        return entry.Info;
    }

    #region Dictionary Implementation

    internal uint _count = 0;

    internal TypeMapping[] _entries = [];

    private int[] _buckets = null!;
    private static readonly uint[] s_primes = [3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919, 1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437, 187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263, 1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369];


    private int _freeList;
    private ulong _fastModMultiplier;
    private int _version, _freeCount;

    private static uint GetPrime(uint min)
    {
        uint[] array = s_primes;

        foreach (uint num in array)
            if (num >= min)
                return num;

        for (uint j = min | 1u; j < uint.MaxValue; j += 2)
            if (IsPrime(j) && (j - 1) % 101 != 0)
                return j;

        return min;
    }

    private static bool IsPrime(uint candidate)
    {
        if ((candidate & (true ? 1u : 0u)) != 0)
        {
            var num = Math.Sqrt(candidate);

            for (int i = 3; i <= num; i += 2)
                if (candidate % i == 0)
                    return false;

            return true;
        }
        return candidate == 2;
    }

    internal uint Initialize(uint capacity)
    {
        typeSet.Initialize(0);
        var prime = GetPrime(capacity);
        var buckets = new int[prime];
        var entries = new TypeMapping[prime];

        _freeList = -1;
#if TARGET_64BIT
        _fastModMultiplier = GetFastModMultiplier(prime);
#endif

        _buckets = buckets;
        _entries = entries;
        return prime;
    }

    private static ulong GetFastModMultiplier(uint divisor) => ulong.MaxValue / divisor + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetBucket(uint hashCode)
    {
        int[] buckets = _buckets;
        return ref buckets[FastMod(hashCode, (uint)buckets.Length, _fastModMultiplier)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FastMod(uint value, uint divisor, ulong multiplier) 
        => (uint)(((multiplier * value >> 32) + 1) * divisor >> 32);

    private void Resize() => Resize(ExpandPrime(_count));

    private void Resize(uint newSize)
    {
        var array = new TypeMapping[newSize];

        Array.Copy(_entries, array, _count);

        _buckets = new int[newSize];
        _fastModMultiplier = GetFastModMultiplier(newSize);

        for (int j = 0; j < _count; j++)
        {
            if (array[j].next >= -1)
            {
                ref int bucket = ref GetBucket(array[j].Id);
                array[j].next = bucket - 1;
                bucket = j + 1;
            }
        }
        _entries = array;
    }

    private static uint ExpandPrime(uint oldSize)
    {
        uint num = 2 * oldSize;
        if (num > 2147483587u && 2147483587u > oldSize)
        {
            return 2147483587u;
        }
        return GetPrime(num);
    }

    internal static bool AreTypeEquals(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        return _comparer.Equals(sourceType, targetType);
    }
    #endregion
}

internal readonly record struct TypeMapInfo(ITypeSymbol membersSource, ITypeSymbol? implementation = null);

internal readonly record struct MapInfo(TypeMapInfo from, TypeMapInfo to, MappingKind mapKind, ApplyOn ignore, bool generate = true);

internal readonly record struct CompilationAndAssemblies(Compilation Compilation, ImmutableArray<MapInfo> OverClass, ImmutableArray<MapInfo> Assembly);