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
    private void CreateTypeMapBuilders
    (
        TypeMappingInfo map,
        ApplyOn ignore,
        string sourceMappingPath,
        string targetMappingPath,
        Member source,
        Member target,
        Span<Member> sourceMembers,
        Span<Member> targetMembers
    )
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
            ttsSpacing = map.SourceType.IsTupleType ? " " : "\r\n            ",
            sttSpacing = map.TargetType.IsTupleType ? " " : "\r\n            ";

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

        if (map.BuildToTargetValue is null || !map.TargetType.IsValueType)
        {
            map.RequiresSTTCall = true;
            map.BuildToTargetValue = val => map.ToTargetMethodName + "(" + val + (isSTTRecursive ? ", depth + 1" + (source.MaxDepth > 1 ? ", " + source.MaxDepth : null) : null) + ")";
        }

        map.BuildToTargetMethod = code =>
        {
            if (map.IsSTTRendered)
                return;

            var maxDepth = target.MaxDepth;

            if (isSTTRecursive && target.MaxDepth == 0)
                maxDepth = target.MaxDepth = 1;

            map.RenderedSTTDefaultMethod = true;

            if(target.Type.HasPublicZeroArgsCtor)
                CreateDefaultMethod(code, isSTTRecursive, maxDepth, toSameType, sttTypeStart, sttTypeEnd, map.ToTargetMethodName, sourceExportFullTypeName, targetExportFullTypeName, source.DefaultBang, sttMembers, hasComplexSTTMembers);

            if (MappingKind.Fill.HasFlag(map.MappingsKind))
            {
                map.RenderedSTTFillMethod = true;

                CreateFillMethod(code, isSTTRecursive, maxDepth, map.TargetType.IsValueType, map.FillTargetMethodName, targetExportFullTypeName, sourceExportFullTypeName, source.DefaultBang, sttMembers, hasComplexSTTMembers);

                foreach (var item in map.TargetType.UnsafePropertyFieldsGetters)
                    item.Render(code);
            }

            if (map.AddSTTTryGet)
            {
                map.RenderedSTTTryGetMethod = true;

                CreateTryGetMethod(code, isSTTRecursive, maxDepth, toSameType, map.TryGetTargetMethodName, map.ToTargetMethodName, sourceExportFullTypeName, targetExportFullTypeName, source.DefaultBang, sttMembers, hasComplexSTTMembers);
            }
        };

        if (map.BuildToSourceValue is null || !map.SourceType.IsValueType)
        {
            map.RequiresTTSCall = true;
            map.BuildToSourceValue = val => map.ToSourceMethodName + "(" + val + (isTTSRecursive ? ", depth + 1" + (target.MaxDepth > 1 ? ", " + target.MaxDepth : null) : null) + ")";
        }

        map.BuildToSourceMethod = code =>
        {
            if (map.IsTTSRendered)
                return;

            if (isTTSRecursive && source.MaxDepth == 0)
                source.MaxDepth = 1;

            map.RenderedTTSDefaultMethod = true;

            if (source.Type.HasPublicZeroArgsCtor)
                CreateDefaultMethod(code, isTTSRecursive, source.MaxDepth, toSameType, ttsTypeStart, ttsTypeEnd, map.ToSourceMethodName, targetExportFullTypeName, sourceExportFullTypeName, target.DefaultBang, ttsMembers, hasComplexTTSMembers);

            if (MappingKind.Fill.HasFlag(map.MappingsKind))
            {
                map.RenderedTTSFillMethod = true;

                CreateFillMethod(code, isTTSRecursive, source.MaxDepth, map.SourceType.IsValueType, map.FillSourceMethodName, sourceExportFullTypeName, targetExportFullTypeName, target.DefaultBang, ttsMembers, hasComplexTTSMembers);

                foreach (var item in map.SourceType.UnsafePropertyFieldsGetters)
                    item.Render(code);
            }

            if (map.AddTTSTryGet)
            {
                map.RenderedTTSTryGetMethod = true;

                CreateTryGetMethod(code, isTTSRecursive, source.MaxDepth, toSameType, map.TryGetSourceMethodName, map.ToSourceMethodName, targetExportFullTypeName, sourceExportFullTypeName, target.DefaultBang, ttsMembers, hasComplexTTSMembers);
            }

            map.BuildToSourceMethod = null;
        };


        var allowLowerCase = map.TargetType.IsTupleType || map.SourceType.IsTupleType;

        foreach (var targetMember in targetMembers)
        {
            foreach (var sourceMember in sourceMembers)
            {
                if (AreNotMappableByDesign(allowLowerCase, sourceMember, targetMember, out var ignoreSource, out var ignoreTarget))
                {
                    if ((targetMember.Type.Id, targetMember.Name) == (sourceMember.Type.Id, sourceMember.Name)) 
                        break;
                    
                    continue;
                }

                if (mapId == GetId(targetMember.Type.Id, targetMember.Type.Id))
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
                        
                        sttMembers += (code, isFill) =>
                        {
                            code.Append(BuildTypeMember(
                                isFill,
                                ref sttComma,
                                sttSpacing,
                                map.BuildToTargetValue,
                                sourceMember,
                                targetMember,
                                map.ToTargetMethodName,
                                map.FillTargetMethodName,
                                map.RequiresSTTCall));

                            if (targetMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += code => nullUnsafeAccesor.Render(code);
                        };
                    }

                    if (!(toSameType && parentIgnoreSource || ignoreSource || map.SourceType.IsInterface))
                    {
                        map.TTSMemberCount++;

                        map.SourceType.IsRecursive = isTTSRecursive = true;
                        hasComplexTTSMembers = true;

                        if (sourceMember.IsNullable)
                            map.AddTTSTryGet = true;
                        
                        ttsMembers += (code, isFill) =>
                        {
                            code.Append(BuildTypeMember(
                                isFill,
                                ref ttsComma,
                                ttsSpacing,
                                map.BuildToSourceValue,
                                targetMember,
                                sourceMember,
                                map.ToSourceMethodName,
                                map.FillSourceMethodName,
                                map.RequiresTTSCall));

                            if (sourceMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += code => nullUnsafeAccesor.Render(code);
                        };
                    }

                    map.CanMap |= map.HasTargetToSourceMap || map.HasTargetToSourceMap;

                    break;
                }

                var memberMap = GetOrAddMapper(targetMember.Type,sourceMember.Type);

                memberMap = ParseTypesMap(
                    memberMap,
                    sourceMember,
                    targetMember,
                    ApplyOn.None,
                    sourceMappingPath,
                    targetMappingPath);

                if (ParseTypesMap(
                    memberMap,
                    sourceMember,
                    targetMember,
                    ApplyOn.None,
                    sourceMappingPath,
                    targetMappingPath) is { CanMap: not false })
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

                        sttMembers += (code, isFill) =>
                        {
                            code.Append(BuildTypeMember(
                                isFill,
                                ref sttComma,
                                sttSpacing,
                                memberMap.BuildToTargetValue,
                                sourceMember,
                                targetMember,
                                memberMap.ToTargetMethodName,
                                memberMap.FillTargetMethodName,
                                memberMap.RequiresSTTCall));

                            if (targetMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += code => nullUnsafeAccesor.Render(code);
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

                        ttsMembers += (code, isFill) =>
                        {
                            code.Append(BuildTypeMember(
                                isFill,
                                ref ttsComma,
                                ttsSpacing,
                                memberMap.BuildToSourceValue,
                                targetMember,
                                sourceMember,
                                memberMap.ToSourceMethodName,
                                memberMap.FillSourceMethodName,
                                memberMap.RequiresTTSCall));

                            if (sourceMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += code => nullUnsafeAccesor.Render(code);
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
            StringBuilder code,
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
    /// <param name=""source"">Data source to be mappped</param>{(isRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : @"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]")}
    public static {targetFullTypeName} {methodName}(this {sourceFullTypeName} source{(
                isRecursive
                    ? $@", int depth = 0, int maxDepth = {maxDepth})
    {{
        if (depth >= maxDepth) 
            return default{defaultSourceBang};
"
                    : $@")
    {{")}
        return {typeStart}");

            members(code, false);

            code.Append($@"{typeEnd};
    }}
");
        }

        void CreateTryGetMethod
        (
            StringBuilder code,
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
    /// <param name=""source"">Data source</param>{(isRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : @"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]")}
    public static bool {methodName}(this {sourceFullTypeName} source, out {targetFullTypeName} target{(
                isRecursive
                    ? $@", int depth = 0, int maxDepth = {maxDepth})
    {{"
                    : $@")
    {{")}
        if (source is {{}} _source)
        {{
            target = {toMethodName}(_source{(isRecursive ? ", depth, maxDepth" : null)});
            return true;
        }}
        target = default{defaultSourceBang};
        return false;
    }}
");
        }

        void CreateFillMethod
        (
            StringBuilder code,
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
    /// <param name=""source"">Data source to be mappped</param>{(isRecursive ? $@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>" : @"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]")}
    public static {_ref}{targetFullTypeName} {fillMethodName}({_ref}this {targetFullTypeName} target, {sourceFullTypeName} source{(
                isRecursive
                    ? $@", int depth = 0, int maxDepth = {maxDepth})
    {{
        if (depth >= maxDepth) 
            return {_ref}target{sourceBang};
"
                    : $@")
    {{")}");

            members(code, true);

            code.Append($@"

        return {_ref}target;
    }}
");
        }
    }
}
