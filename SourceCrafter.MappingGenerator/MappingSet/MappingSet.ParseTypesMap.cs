using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;

using System;
using System.Text;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet
{
    private TypeMapping DiscoverTypeMaps
    (
        TypeMapping map,
        MemberMeta source,
        MemberMeta target,
        IgnoreBind ignore,
        string sourceMappingPath,
        string targetMappingPath)
    {
        map.EnsureDirection(ref target, ref source);

        GetNullability(target, map.TargetType.Type, source, map.SourceType.Type);

        if (map.CanMap is not null)
        {
            if (map.TargetType is { IsRecursive: false, IsStruct: false, IsPrimitive: false })
                map.TargetType.IsRecursive = IsRecursive(targetMappingPath + map.TargetType.Id + "+", map.TargetType.Id);

            if (map.SourceType is { IsRecursive: false, IsStruct: false, IsPrimitive: false })
                map.SourceType.IsRecursive = (map.AreSameType && map.TargetType.IsRecursive) || IsRecursive(sourceMappingPath + map.SourceType.Id + "+", map.SourceType.Id);

            return map;
        }

        if (map.IsCollection)
        {
            MemberMeta
                targetItem = new(target.Id, target.Name + "Item", target.Type.CollectionInfo.IsItemNullable),
                sourceItem = new(source.Id, source.Name + "Item", source.Type.CollectionInfo.IsItemNullable);

            if (map.ItemMap.CanMap is false || !(source.Type.CollectionInfo.ItemDataType.HasZeroArgsCtor && target.Type.CollectionInfo.ItemDataType.HasZeroArgsCtor))
            {
                map.CanMap = map.IsCollection = false;

                return map;
            }

            if (targetItem.IsNullable)
                map.ItemMap.AddSourceTryGet = true;

            if (sourceItem.IsNullable)
                map.ItemMap.AddTargetTryGet = true;

            var itemMap = DiscoverTypeMaps(
                map.ItemMap,
                sourceItem,
                targetItem,
                IgnoreBind.None,
                sourceMappingPath,
                targetMappingPath);

            if (map.CanMap is null && true ==
                (map.CanMap =
                    ignore is not (IgnoreBind.Target or IgnoreBind.Both) &&
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
                    (ignore is not (IgnoreBind.Source or IgnoreBind.Both) &&
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
                map.ExtraMappings += itemMap.BuildMethods;
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
            if (ignore is not IgnoreBind.Target or IgnoreBind.Both && targetScalarConversion.exists)
            {
                var scalar = targetScalarConversion.isExplicit ? $"({map.TargetType.FullName}){{0}}" : "{0}";

                map.BuildTargetValue = (code, value) => code.AppendFormat(scalar, value);

                canMap = map.TargetHasScalarConversion = true;
            }

            if (ignore is not IgnoreBind.Source or IgnoreBind.Both && sourceScalarConversion.exists)
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
            MemberMeta sourceKeyMember, sourceValueMember, targetKeyMember, targetValueMember;

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

            TypeMeta
                sourceKeyType = sourceKeyMember.Type,
                sourceValueType = sourceValueMember.Type,
                targetKeyType = targetKeyMember.Type,
                targetValueType = targetValueMember.Type;

            TypeMapping
                keyMapping = GetOrAdd(sourceKeyType, targetKeyType),
                valueMapping = GetOrAdd(sourceValueType, targetValueType);

            keyMapping = DiscoverTypeMaps(keyMapping, sourceValueMember, sourceKeyMember, IgnoreBind.None, sourceMappingPath, targetMappingPath);
            valueMapping = DiscoverTypeMaps(valueMapping, targetValueMember, targetKeyMember, IgnoreBind.None, sourceMappingPath, targetMappingPath);

            if (keyMapping.CanMap is false && valueMapping.CanMap is false) map.CanMap = false;

            var (checkKeyPropNull, checkValuePropNull, checkKeyFieldNull, checkValueFieldNull) = 
            (
                !(sourceKeyMember.IsNullable && sourceKeyMember.Type.IsStruct || !sourceValueMember.IsNullable), 
                !(targetKeyMember.IsNullable && targetKeyMember.Type.IsStruct || !targetValueMember.IsNullable),
                !(sourceValueMember.IsNullable && sourceValueMember.Type.IsStruct || !sourceKeyMember.IsNullable),
                !(targetValueMember.IsNullable && targetValueMember.Type.IsStruct || !targetKeyMember.IsNullable)
            );

            var (keyPropName, valuePropName, targetKeyFieldName, targetValueFieldName) = 
            (
                 sourceKeyMember.Name, 
                 targetKeyMember.Name, 
                 sourceValueMember.Name, 
                 targetValueMember.Name
            );

            KeyValueMappings
                stt = map.TargetKeyValueMap = new(
                    (code, id) => GenerateValue(
                        code,
                        id + "." + targetKeyMember.Name,
                        keyMapping.BuildTargetValue,
                        checkKeyFieldNull,
                        false,
                        targetKeyMember.Type.IsValueType,
                        sourceKeyMember.Bang,
                        sourceKeyMember.DefaultBang),
                    (code, id) => GenerateValue(
                        code,
                        id + "." + targetValueMember.Name,
                        valueMapping.BuildTargetValue,
                        checkValueFieldNull,
                        false,
                        targetValueMember.Type.IsValueType,
                        targetKeyMember.Bang,
                        targetKeyMember.DefaultBang)),
                tts = map.SourceKeyValueMap = new(
                    (code, id) => GenerateValue(
                        code,
                        id + "." + sourceKeyMember.Name,
                        keyMapping.BuildSourceValue,
                        checkKeyPropNull,
                        false,
                        sourceKeyMember.Type.IsValueType,
                        sourceValueMember.Bang,
                        sourceValueMember.DefaultBang),
                    (code, id) => GenerateValue(
                        code,
                        id + "." + sourceValueMember.Name,
                        valueMapping.BuildSourceValue,
                        checkValuePropNull,
                        false,
                        sourceValueMember.Type.IsValueType,
                        targetValueMember.Bang,
                        targetValueMember.DefaultBang));

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

    private void GetKeyValueProps(Span<MemberMeta> members, out MemberMeta keyProp, out MemberMeta valueProp)
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
        var t = 1;

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
