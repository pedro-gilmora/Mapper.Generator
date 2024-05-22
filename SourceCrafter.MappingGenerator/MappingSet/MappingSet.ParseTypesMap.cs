using Microsoft.CodeAnalysis;

using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        GetNullability(target, map.TargetType._type, source, map.SourceType._type);

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

        if (map.IsCollection)
        {
            int targetItemId = target.Type.CollectionInfo.ItemDataType.Id,
                sourceItemId = source.Type.CollectionInfo.ItemDataType.Id;

            Member
                targetItem = new(target.Id, target.Name + "Item", target.Type.CollectionInfo.IsItemNullable),
                sourceItem = new(source.Id, source.Name + "Item", source.Type.CollectionInfo.IsItemNullable);

            var itemMap = GetOrAddMapper(target.Type.CollectionInfo.ItemDataType, source.Type.CollectionInfo.ItemDataType);

            map.ItemMapId = itemMap.Id;

            itemMap.TargetType.IsRecursive |= itemMap.TargetType.IsRecursive;
            itemMap.SourceType.IsRecursive |= itemMap.SourceType.IsRecursive;

            if (targetItem.IsNullable)
                itemMap.AddTTSTryGet = true;

            if (sourceItem.IsNullable)
                itemMap.AddSTTTryGet = true;

            //target.DataType = map.TargetType;
            //source.DataType = map.SourceType;
            //target.ItemDataType = itemMap.TargetType;
            //source.ItemDataType = itemMap.SourceType;

            if (itemMap.CanMap is false ||
                !(source.Type.CollectionInfo.ItemDataType.HasPublicZeroArgsCtor && target.Type.CollectionInfo.ItemDataType.HasPublicZeroArgsCtor))
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

            if (itemMap.CanMap is true && map.CanMap is null && true ==
                (map.CanMap =
                    ignore is not ApplyOn.Target or ApplyOn.Both &&
                    (!itemMap.TargetType.IsInterface && (map.RequiresSTTCall = CreateCollectionMapBuilders(
                        source,
                        target,
                        sourceItem,
                        targetItem,
                        map.RequiresSTTCall,
                        source.Type.CollectionInfo,
                        target.Type.CollectionInfo,
                        map.STTCollectionMapping,
                        itemMap.BuildToTargetValue,
                        itemMap.STTKeyValueMapping,
                        ref map.BuildToTargetValue,
                        ref map.BuildToTargetMethod)))
                    |
                    (ignore is not ApplyOn.Source or ApplyOn.Both &&
                     !itemMap.SourceType.IsInterface && (map.RequiresTTSCall = CreateCollectionMapBuilders(
                        target,
                        source,
                        targetItem,
                        sourceItem,
                        map.RequiresTTSCall,
                        target.Type.CollectionInfo,
                        source.Type.CollectionInfo,
                        map.TTSCollectionMapping,
                        itemMap.BuildToSourceValue,
                        itemMap.TTSKeyValueMapping,
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

            return map;
        }

        var canMap = false;

        bool isSourceObject = map.SourceType.NonGenericFullName == "object",
            isTargetObject = map.TargetType.NonGenericFullName == "object";

        if (!(map.SourceType is not { DictionaryOwned: false, IsKeyValueType: false }
            || map.TargetType is not { DictionaryOwned: false, IsKeyValueType: false }
            || !map.SourceType.HasConversionTo(map.TargetType, out var exists, out var isExplicit)
            & !map.TargetType.HasConversionTo(map.SourceType, out var reverseExists, out var isReverseExplicit)
            && !isSourceObject & !isTargetObject))
        {
            var isObjectMap = isSourceObject || isTargetObject;

            isExplicit |= isSourceObject && !isTargetObject;
            isReverseExplicit |= !isSourceObject && isTargetObject;

            if (ignore is not ApplyOn.Target or ApplyOn.Both && (exists || isObjectMap))
            {
                var scalar = isExplicit
                    ? $@"({map.TargetType.FullName}){{0}}"
                    : $@"{{0}}";

                map.BuildToTargetValue = value => scalar.Replace("{0}", value);

                canMap = map.HasToTargetScalarConversion = true;
            }

            if (ignore is not ApplyOn.Source or ApplyOn.Both && (reverseExists || isObjectMap))
            {
                var scalar = isReverseExplicit
                    ? $@"({map.SourceType.FullName}){{0}}"
                    : $@"{{0}}";

                map.BuildToSourceValue = value => scalar.Replace("{0}", value);

                canMap = map.HasToSourceScalarConversion = true;
            }
        }

        if (map.TargetType.IsPrimitive || map.SourceType.IsPrimitive)
        {
            map.CanMap = canMap;
            return map;
        }

        switch (map)
        {
            case { TargetType: { DictionaryOwned: true, IsKeyValueType: true } } or { SourceType: { DictionaryOwned: true, IsKeyValueType: true } }:
                CreateKeyValueMapBuilder(map);
                return map;
            default:
                CreateTypeMapBuilders(map, ignore, sourceMappingPath, targetMappingPath, source, target, map.SourceType.Members, map.TargetType.Members);
                return map;
        }

        void CreateKeyValueMapBuilder
        (
            TypeMappingInfo map)
        {
            Member sourceKeyMember, sourceValueMember, targetKeyMember, targetValueMember;

            switch ((
                map.SourceType is { IsTupleType: true, Members.Length: 2 },
                map.TargetType is { IsTupleType: true, Members.Length: 2 }
            ))
            {
                case (false, false):
                    GetKeyValueProps(map.SourceType.Members, out sourceKeyMember, out sourceValueMember);
                    GetKeyValueProps(map.TargetType.Members, out targetKeyMember, out targetValueMember);
                    break;
                case (false, true):
                    GetKeyValueProps(map.SourceType.Members, out sourceKeyMember, out sourceValueMember);

                    (targetKeyMember, targetValueMember) = (map.TargetType.Members[0], map.TargetType.Members[1]);

                    if (targetValueMember.Name.ToLower().Contains("key"))
                        (targetValueMember, targetKeyMember) = (targetKeyMember, targetValueMember);

                    break;
                case (true, false):
                    GetKeyValueProps(map.TargetType.Members, out sourceKeyMember, out sourceValueMember);

                    (targetKeyMember, targetValueMember) = (map.SourceType.Members[0], map.SourceType.Members[1]);

                    if (targetValueMember.Name.ToLower().Contains("key"))
                        (targetValueMember, targetKeyMember) = (targetKeyMember, targetValueMember);

                    break;
                default:
                    sourceKeyMember = sourceValueMember = targetKeyMember = targetValueMember = null!;
                    map.CanMap = false;
                    return;
            }

            TypeData
                sourceKeyType = sourceKeyMember.Type,
                sourceValueType = sourceValueMember.Type,
                targetKeyType = targetKeyMember.Type,
                targetValueType = targetValueMember.Type;

            TypeMappingInfo
                keyMapping = GetOrAddMapper(sourceKeyType, targetKeyType),
                valueMapping = GetOrAddMapper(sourceValueType, targetValueType);

            keyMapping = ParseTypesMap(keyMapping, sourceValueMember, sourceKeyMember, ApplyOn.None, sourceMappingPath, targetMappingPath);
            valueMapping = ParseTypesMap(valueMapping, targetValueMember, targetKeyMember, ApplyOn.None, sourceMappingPath, targetMappingPath);

            if (keyMapping.CanMap is false && valueMapping.CanMap is false) map.CanMap = false;

            var checkKeyPropNull = (!sourceKeyMember.IsNullable || !sourceKeyMember.Type.IsStruct) && sourceValueMember.IsNullable;
            var checkValuePropNull = (!targetKeyMember.IsNullable || !targetKeyMember.Type.IsStruct) && targetValueMember.IsNullable;

            var checkKeyFieldNull = (!sourceValueMember.IsNullable || !sourceValueMember.Type.IsStruct) && sourceKeyMember.IsNullable;
            var checkValueFieldNull = (!targetValueMember.IsNullable || !targetValueMember.Type.IsStruct) && targetKeyMember.IsNullable;

            string keyPropName = sourceKeyMember.Name,
                valuePropName = targetKeyMember.Name,
                targetKeyFieldName = sourceValueMember.Name,
                targetValueFieldName = targetValueMember.Name;

            KeyValueMappings
                stt = map.STTKeyValueMapping = new(
                    src => GenerateValue(src + "." + targetKeyMember.Name, keyMapping.BuildToTargetValue, checkKeyFieldNull, false, targetKeyMember.Type.IsValueType, sourceKeyMember.Bang, sourceKeyMember.DefaultBang),
                    src => GenerateValue(src + "." + targetValueMember.Name, valueMapping.BuildToTargetValue, checkValueFieldNull, false, targetValueMember.Type.IsValueType, targetKeyMember.Bang, targetKeyMember.DefaultBang)),
                tts = map.TTSKeyValueMapping = new(
                    src => GenerateValue(src + "." + sourceKeyMember.Name, keyMapping.BuildToSourceValue, checkKeyPropNull, false, sourceKeyMember.Type.IsValueType, sourceValueMember.Bang, sourceValueMember.DefaultBang),
                    src => GenerateValue(src + "." + sourceValueMember.Name, valueMapping.BuildToSourceValue, checkValuePropNull, false, sourceValueMember.Type.IsValueType, targetValueMember.Bang, targetValueMember.DefaultBang));

            var buildTargetValue = map.BuildToTargetValue = sb =>
                string.Format(@" new {0}({1}, {2})", map.TargetType.NotNullFullName, stt.Key(sb), stt.Value(sb));

            map.BuildToTargetMethod = sb =>
                sb.AppendFormat(@"
        public static {0} {1}(ref this {2} source)
        {{
            return {3};
        }}",
                    map.TargetType.NotNullFullName,
                    map.ToTargetMethodName,
                    map.SourceType.NotNullFullName,
                    buildTargetValue("source"));

            var buildSourceValue = map.BuildToSourceValue = sb =>
                string.Format(@"({0}, {1})",
                    tts.Key(sb),
                    tts.Value(sb));

            map.BuildToSourceMethod = sb =>
                sb.AppendFormat(@"
        public static {0} {1}(ref this {2} source)
        {{
            return {3};
        }}",
                    map.SourceType.NotNullFullName,
                    map.ToSourceMethodName,
                    map.TargetType.NotNullFullName,
                    buildSourceValue("source"));

            map.CanMap = true;

        }
    }

    private void GetKeyValueProps(Span<Member> members, out Member keyProp, out Member valueProp)
    {
        keyProp = null!;
        valueProp = null!;

        foreach (var member in members)
        {
            if (!member.IsProperty) continue;

            switch (member.Name)
            {
                case "Key":
                    keyProp = member;

                    if (valueProp != null) return;

                    continue;
                case "Value":
                    valueProp = member;

                    if (keyProp != null) return;

                    continue;
                default:
                    continue;
            }
        }
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
