using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet
{
    TypeMappingInfo ParseTypesMap
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

            itemMap = ParseTypesMap(
                itemMap,
                sourceItem,
                targetItem,
                ApplyOn.None,
                sourceMappingPath,
                targetMappingPath);

            if (itemMap.CanMap is true && map.CanMap is null && true == (map.CanMap =
                ignore is not ApplyOn.Target or ApplyOn.Both &&
                (!itemMap.TargetType.IsInterface && (map.RequiresSTTCall = CreateCollectionMapBuilders(
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
                !itemMap.SourceType.IsInterface && (map.RequiresTTSCall = CreateCollectionMapBuilders(
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
                    CreateTypeMapBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.TupleElements, map.TargetType.TupleElements);
                    break;
                case (true, false):
                    CreateTypeMapBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.Members, map.TargetType.TupleElements);
                    break;
                case (false, true):
                    CreateTypeMapBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.TupleElements, map.TargetType.Members);
                    break;
                default:
                    CreateTypeMapBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.Members, map.TargetType.Members);
                    break;
            }



        }
    exit:
        return map;
    }

    private bool IsRecursive(string s, int id)
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
}
