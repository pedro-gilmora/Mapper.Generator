using SourceCrafter.Bindings.Constants;
using System;
using System.Text;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet
{

    private bool CreateCollectionMapBuilders(
        Member target,
        Member sourceItem,
        Member targetItem,
        bool call,
        CollectionInfo sourceCollInfo,
        CollectionInfo targetCollInfo,
        CollectionMapping ltrInfo,
        ValueBuilder itemValueCreator,
        ref ValueBuilder valueCreator,
        ref Action<StringBuilder>? methodCreator)
    {
        string
            targetFullTypeName = targetCollInfo.DataType.ExportNotNullFullName,
            sourceFullTypeName = sourceCollInfo.DataType.ExportNotNullFullName,
            targetItemFullTypeName = targetCollInfo.ItemDataType.FullName,
            countProp = sourceCollInfo.CountProp,
            copyMethodName = ltrInfo.MethodName,
            updateMethodName = ltrInfo.FillMethodName;

        bool IsRecursive(out int maxDepth)
        {
            var isRecursive = targetCollInfo.ItemDataType.IsRecursive;

            maxDepth = target.MaxDepth;

            if (targetCollInfo.ItemDataType.IsRecursive && target.MaxDepth == 0)
                maxDepth = target.MaxDepth = 1;

            return isRecursive;
        }

        string buildCopy()
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
    /// <param name=""source"">Data source to be mappped</param>{(targetCollInfo.ItemDataType.IsRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : null)}
    public static {targetFullTypeName} {copyMethodName}(this {sourceFullTypeName} source{(targetCollInfo.ItemDataType.IsRecursive ? $@", int depth = 0, int maxDepth = {target.MaxDepth})
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
        var target = new {targetItemFullTypeName}[aux];"
                : $@"int len = source.{countProp};
        var target = new {targetItemFullTypeName}[len];"
            : $@"var target = new {initType};")}
";

            if (ltrInfo.Iterator == "for")
            {
                method += $@"
        for (int i = 0; i < len; i++)
        {{
            target[i] = {GenerateValue("source[i]", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)};
        }}

        return target{suffix};
    }}
";
            }
            else
            {
                method += $@"
        foreach (var item in source)
        {{";
                if (ltrInfo.CreateArray)
                {
                    method += $@"
            target[len{(ltrInfo.Redim ? null : "++")}] = {GenerateValue("item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)};";
                    method += ltrInfo.Redim
                        //redim array
                        ? $@"

            if (aux == ++len)
                global::System.Array.Resize(ref target, aux *= 2);
        }}
        
        return (len < aux ? target[..len] : target){suffix};
    }}"
                        //normal ending
                        : $@"
        }}

        return target{suffix};
    }}";

                }
                else
                {
                    method += $@"
            target.{ltrInfo.Method}({GenerateValue("item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)});
        }}

        return {returnExpr("target")};
    }}
";
                }
            }

            return method;
        }

        string buildUpdate()
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
    /// <param name=""source"">Data source to be mappped</param>{(targetCollInfo.ItemDataType.IsRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : null)}
    public static {targetFullTypeName} {copyMethodName}(this {sourceFullTypeName} target, {targetFullTypeName} source{(targetCollInfo.ItemDataType.IsRecursive ? $@", int depth = 0, int maxDepth = {target.MaxDepth})
    {{
        if (depth >= maxDepth) 
            return {(ltrInfo.CreateArray
            ? $"global::System.Array.Empty<{targetItemFullTypeName}>()"
            : defaultType
            )};

        int inputLen = source.{countProp};
" : $@")
    {{
        ")}
        {(ltrInfo.CreateArray
            ? ltrInfo.Redim
                ? $@"int len = 0, aux = 16, "
                : $@"
        len = source.{countProp};"
            : null)}
";

            if (ltrInfo.Iterator == "for")
            {
                method += $@"
        int oldLen = source.{countProp};
        
        for (int i = 0; i < len; i++)
        {{
            target[i] = {GenerateValue("source[i]", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)};
        }}

        return target{suffix};
    }}
";
            }
            else
            {
                method += $@"
        foreach (var item in source)
        {{";
                if (ltrInfo.CreateArray)
                {
                    method += $@"
            target[len{(ltrInfo.Redim ? null : "++")}] = {GenerateValue("item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)};";
                    method += ltrInfo.Redim
                        //redim array
                        ? $@"

            if (aux == ++len)
                global::System.Array.Resize(ref target, aux *= 2);
        }}
        
        return (len < aux ? target[..len] : target){suffix};
    }}"
                        //normal ending
                        : $@"
        }}

        return target{suffix};
    }}";

                }
                else
                {
                    method += $@"
            target.{ltrInfo.Method}({GenerateValue("item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang)});
        }}

        return {returnExpr("target")};
    }}
";
                }
            }

            return method;
        }

        var called = false;

        valueCreator = value => copyMethodName + "(" + value + (IsRecursive(out var maxDepth) ? ", -1 + depth + 1" + (maxDepth > 1 ? ", " + maxDepth : null) : null) + ")";
        methodCreator = code => { if (!called) { called = true; code.Append(buildCopy()); } };

        return true;
    }
}
