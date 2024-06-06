using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;

using System;
using System.Text;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet
{
    TypeMapping ParseTypesMap
    (
        TypeMapping map,
        MemberMetadata source,
        MemberMetadata target,
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

            MemberMetadata
                targetItem = new(target.Id, target.Name + "Item", target.Type.CollectionInfo.IsItemNullable),
                sourceItem = new(source.Id, source.Name + "Item", source.Type.CollectionInfo.IsItemNullable);

            var itemMap = GetOrAddMapper(target.Type.CollectionInfo.ItemDataType, source.Type.CollectionInfo.ItemDataType);

            map.ItemMapId = itemMap.Id;

            itemMap.TargetType.IsRecursive |= itemMap.TargetType.IsRecursive;
            itemMap.SourceType.IsRecursive |= itemMap.SourceType.IsRecursive;

            if (targetItem.IsNullable)
                itemMap.AddSourceTryGet = true;

            if (sourceItem.IsNullable)
                itemMap.AddTargetTryGet = true;

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
                    (!itemMap.TargetType.IsInterface && (map.TargetRequiresMapper = CreateCollectionMapBuilders(
                        source,
                        target,
                        sourceItem,
                        targetItem,
                        map.TargetRequiresMapper,
                        source.Type.CollectionInfo,
                        target.Type.CollectionInfo,
                        map.TargetCollectionMap,
                        itemMap.BuildTargetValue,
                        itemMap.TargetKeyValueMap,
                        ref map.BuildTargetValue,
                        ref map.BuildTargetMethod)))
                    |
                    (ignore is not ApplyOn.Source or ApplyOn.Both &&
                    !itemMap.SourceType.IsInterface && (map.SourceRequiresMapper = CreateCollectionMapBuilders(
                        target,
                        source,
                        targetItem,
                        sourceItem,
                        map.SourceRequiresMapper,
                        target.Type.CollectionInfo,
                        source.Type.CollectionInfo,
                        map.SourceCollectionMap,
                        itemMap.BuildSourceValue,
                        itemMap.SourceKeyValueMap,
                        ref map.BuildSourceValue,
                        ref map.BuildSourceMethod))))
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

        if (map.SourceType.HasConversionTo(map.TargetType, out var targetScalarConversion, out var sourceScalarConversion))
        {
            if (ignore is not ApplyOn.Target or ApplyOn.Both && targetScalarConversion.exists)
            {
                var scalar = targetScalarConversion.isExplicit
                    ? $@"({map.TargetType.FullName}){{0}}"
                    : "{0}";

                map.BuildTargetValue = (code, value) => code.AppendFormat(scalar, value);

                canMap = map.TargetHasScalarConversion = true;
            }

            if (ignore is not ApplyOn.Source or ApplyOn.Both && sourceScalarConversion.exists)
            {
                var scalar = sourceScalarConversion.isExplicit
                    ? $@"({map.SourceType.FullName}){{0}}"
                    : "{0}";

                map.BuildSourceValue = (code, value) => code.AppendFormat(scalar, value);

                canMap = map.SourceHasScalarConversion = true;
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

        void CreateKeyValueMapBuilder(TypeMapping map)
        {
            MemberMetadata sourceKeyMember, sourceValueMember, targetKeyMember, targetValueMember;

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

            TypeMetadata
                sourceKeyType = sourceKeyMember.Type,
                sourceValueType = sourceValueMember.Type,
                targetKeyType = targetKeyMember.Type,
                targetValueType = targetValueMember.Type;

            TypeMapping
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
                stt = map.TargetKeyValueMap = new(
                    (code, id) => GenerateValue(code, id + "." + targetKeyMember.Name, keyMapping.BuildTargetValue, checkKeyFieldNull, false, targetKeyMember.Type.IsValueType, sourceKeyMember.Bang, sourceKeyMember.DefaultBang),
                    (code, id) => GenerateValue(code, id + "." + targetValueMember.Name, valueMapping.BuildTargetValue, checkValueFieldNull, false, targetValueMember.Type.IsValueType, targetKeyMember.Bang, targetKeyMember.DefaultBang)),
                tts = map.SourceKeyValueMap = new(
                    (code, id) => GenerateValue(code, id + "." + sourceKeyMember.Name, keyMapping.BuildSourceValue, checkKeyPropNull, false, sourceKeyMember.Type.IsValueType, sourceValueMember.Bang, sourceValueMember.DefaultBang),
                    (code, id) => GenerateValue(code, id + "." + sourceValueMember.Name, valueMapping.BuildSourceValue, checkValuePropNull, false, sourceValueMember.Type.IsValueType, targetValueMember.Bang, targetValueMember.DefaultBang));

            var buildTargetValue = map.BuildTargetValue = (code, sb) =>
            {
                code.Append(" new ").Append(map.TargetType.NotNullFullName).Append(@"("); stt.Key(code, sb); code.Append(", "); stt.Value(code, sb);
            };

            map.BuildTargetMethod = (StringBuilder code, ref RenderFlags isRendered) =>
            {
                if (isRendered.defaultMethod)
                    return;

                isRendered.defaultMethod = true;

                code
                    .Append(@"
    public static ")
                    .Append(map.TargetType.NotNullFullName)
                    .AddSpace()
                    .Append(map.ToTargetMethodName)
                    .Append("(ref this ")
                    .Append(map.SourceType.NotNullFullName)
                    .Append(@" source)
    {
        return ");

                buildTargetValue(code, "source");

                code.Append(@";
    }");
            };

            var buildSourceValue = map.BuildSourceValue = (code, sb) =>
            {
                code.Append("(");
                tts.Key(code, sb);
                code.Append(", ");
                tts.Value(code, sb); 
                code.Append(")");
            };

            map.BuildSourceMethod = (StringBuilder code, ref RenderFlags isRendered) =>
            {
                if (isRendered.defaultMethod)
                    return;

                isRendered.defaultMethod = true;

                code
                    .Append(@"
    public static ")
                    .Append(map.SourceType.NotNullFullName)
                    .AddSpace()
                    .Append(map.ToSourceMethodName)
                    .Append("(ref this ")
                    .Append(map.TargetType.NotNullFullName)
                    .Append(@" source)
    {
        return ");

                buildSourceValue(code, "source");

                code.Append(@";
    }");
            };

            map.CanMap = true;

        }
    }

    private void GetKeyValueProps(Span<MemberMetadata> members, out MemberMetadata keyProp, out MemberMetadata valueProp)
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
