using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;

using System.Text;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet
{

    private bool CreateCollectionMapBuilders(
        MemberMeta source,
        MemberMeta target,
        MemberMeta sourceItem,
        MemberMeta targetItem,
        bool call,
        CollectionInfo sourceCollInfo,
        CollectionInfo targetCollInfo,
        CollectionMapping collMapInfo,
        ValueBuilder itemValueCreator,
        KeyValueMappings? keyValueMapping,
        ref ValueBuilder valueCreator,
        ref MethodRenderer? methodCreator)
    {
        string
            targetFullTypeName = target.Type.ExportNotNullFullName,
            sourceFullTypeName = source.Type.ExportNotNullFullName,
            targetItemFullTypeName = targetCollInfo.ItemDataType.FullName,
            countProp = sourceCollInfo.CountProp,
            copyMethodName = collMapInfo.MethodName,
            updateMethodName = collMapInfo.FillMethodName;

        bool IsRecursive(out int maxDepth)
        {
            var isRecursive = targetCollInfo.ItemDataType.IsRecursive;

            maxDepth = target.MaxDepth;

            if (targetCollInfo.ItemDataType.IsRecursive && target.MaxDepth == 0)
                maxDepth = target.MaxDepth = 1;

            return isRecursive;
        }

        var isFor = collMapInfo.Iterator == "for";

        void BuildCopy(StringBuilder code, ref RenderFlags isRendered)
        {
            if (isRendered.defaultMethod)
                return;

            isRendered.defaultMethod = true;

            string 
                targetExportFullXmlDocTypeName = targetFullTypeName.Replace("<", "{").Replace(">", "}"),
                sourceExportFullXmlDocTypeName = sourceFullTypeName.Replace("<", "{").Replace(">", "}"),
                underlyingCollectionType = $"global::System.Collections.Generic.List<{targetItemFullTypeName}>()";

            (var defaultType, var initType, var returnExpr) = (targetCollInfo.Type, target.Type.IsInterface) switch
            {
                (EnumerableType.ReadOnlyCollection, true) =>
                    ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyReadOnlyCollection", underlyingCollectionType, new ValueBuilder((code, v) => code.Append("new global::System.Collections.ObjectModel.ReadOnlyCollection<").Append(targetItemFullTypeName).Append(">(").Append(v).Append(")"))),
                (EnumerableType.Collection, true) =>
                    ($"global::SourceCrafter.Bindings.CollectionExtensions<{targetItemFullTypeName}>.EmptyCollection", underlyingCollectionType, new ValueBuilder((code, v) => code.Append(v))),
                _ =>
                    ("new " + targetFullTypeName + "()", targetFullTypeName + "()", new ValueBuilder((code, v) => code.Append(v)))
            };

            //User? <== UserDto?
            var checkNull = (!targetItem.IsNullable || !targetCollInfo.ItemDataType.IsStruct) && sourceItem.IsNullable;

            string? suffix = (sourceCollInfo.Type, targetCollInfo.Type) is (not EnumerableType.Array, EnumerableType.ReadOnlySpan) ? ".AsSpan()" : null,
                sourceBang = sourceItem.Bang,
                defaultSourceBang = sourceItem.DefaultBang;

            if (targetCollInfo.IsDictionary)
            {
                code.Append(@"
    /// <summary>
    /// Creates a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based from a given <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/>
    /// </summary>
    /// <param name=""source"">Data source to be mapped</param>");

                if (targetCollInfo.ItemDataType.IsRecursive)
                {
                    code.Append(@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");
                }

                code.Append(@"
    public static ").Append(targetFullTypeName).AddSpace().Append(copyMethodName).Append("(this ").Append(sourceFullTypeName).Append(" source");

                if (targetCollInfo.ItemDataType.IsRecursive)
                {
                    code.Append(@", int depth = 0, int maxDepth = ").Append(target.MaxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return ").Append(defaultType).Append(@";
");
                }
                else
                {
                    code.Append(@")
    {");
                }

                code.Append(@"
        var target = ").Append(defaultType).Append(@";
        
        foreach (var item in source)
        {
            target[");

                keyValueMapping!.Key.Invoke(code, "item");

                code.Append("] = ");

                keyValueMapping!.Value(code, "item");

                code.Append(@";
        }

        return target;
    }
");
                return;
            }

            code.Append(@"
    /// <summary>
    /// Creates a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based from a given <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/>
    /// </summary>
    /// <param name=""source"">Source instance to be mapped</param>");

            if (targetCollInfo.ItemDataType.IsRecursive)
                code.Append(@"
    /// <param name=""depth"">Depth index for recursive control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");

            code.Append(@"
    public static ").Append(targetFullTypeName).AddSpace().Append(copyMethodName).Append("(this ").Append(sourceFullTypeName).Append(@" source");

            if (targetCollInfo.ItemDataType.IsRecursive)
            {
                code.Append(@", int depth = 0, int maxDepth = ").Append(target.MaxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return ");

                if (collMapInfo.CreateArray)
                {
                    code.Append(@"global::System.Array.Empty<").Append(targetItemFullTypeName).Append(">()");
                }
                else
                {
                    code.Append(defaultType);
                }

                code.Append(@";
");
            }
            else 
            {
                code.Append(@")
    {");
            }

            if (collMapInfo.CreateArray)
            {
                if (collMapInfo.Redim)
                {
                    code.Append(@"
        int len = 0, aux = 16;
        var target = new ").Append(targetItemFullTypeName).Append(@"[aux];
");
                }
                else
                {
                    code.Append(@"
        int len = ");

                    if (isFor)
                    {
                        code.Append("source.").Append(countProp);
                    }
                    else
                    {
                        code.Append("0");
                    }

                    code.Append(@";
        var target = new ").Append(targetItemFullTypeName).Append('[');

                    if (isFor)
                    {
                        code.Append("len");
                    }
                    else
                    {
                        code.Append("source.").Append(countProp);
                    }

                    code.Append(@"];
");
                }
            }
            else
            {
                code.Append(@"
        var target = new ").Append(initType).Append(';').Append(@"
");
            }

            if (isFor)
            {
                code.Append(@"
        for (int i = 0; i < len; i++)
        {
            target[i] = "); 
                
                GenerateValue(code, "source[i]", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang);
                
                code.Append(@";
        }

        return target").Append(suffix).Append(@";
    }
");
            }
            else
            {
                code.Append(@"
        foreach (var item in source)
        {");

                if (collMapInfo.CreateArray)
                {
                    code.Append(@"
            target[len");
                    
                    if (!collMapInfo.Redim)
                    {
                        code.Append("++");
                    }

                    code.Append("] = "); 
                    
                    GenerateValue(code, "item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang);

                    code.Append(";");

                    if (collMapInfo.Redim)
                    {
                        //redim array
                        code.Append(@"

            if (aux == ++len)
                global::System.Array.Resize(ref target, aux *= 2);
        }
        
        return (len < aux ? target[..len] : target)").Append(suffix).Append(@";
    }
");
                    }
                    //normal ending
                    else
                    {
                        code.Append(@"
        }

        return target").Append(suffix).Append(@";
    }
");
                    }
                }
                else
                {
                    code.Append(@"
            target.").Append(collMapInfo.Method);

                    GenerateValue(code, "item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang); code.Append(@");
        }

        return ");
                    
                    returnExpr(code, "target");
                    
                    code.Append(@";
    }
");
                }
            }
        }

        valueCreator = (code, value) =>
        {
            code.Append(copyMethodName).Append("(").Append(value);

            if (IsRecursive(out var maxDepth)) 
            {
                code.Append(", -1 + depth + 1");
                
                if (maxDepth > 1) 
                    code.Append(", ").Append(maxDepth); 
            }

            code.Append(")");
        };

        methodCreator = BuildCopy;

        return true;
    }
}
