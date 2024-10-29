using SourceCrafter.Bindings.Constants;
using SourceCrafter.Bindings.Helpers;

using System;
using System.Text;

namespace SourceCrafter.Bindings;

internal sealed partial class MappingSet
{
    private void CreateTypeMapBuilders
    (
        TypeMapping map,
        ApplyOn ignore,
        string sourceMappingPath,
        string targetMappingPath,
        MemberMeta source,
        MemberMeta target,
        Span<MemberMeta> sourceMembers,
        Span<MemberMeta> targetMembers
    )
    {
        if (map is { IsCollection: not true, TargetType.IsInterface: true, SourceType.IsInterface: true })
            return;

        string
            sourceExportFullTypeName = map.SourceType.ExportNotNullFullName,
            targetExportFullTypeName = map.TargetType.ExportNotNullFullName,
            ttsTypeStart = map.SourceType.IsTupleType ? TupleStart : string.Format(TypeStart, map.SourceType.NotNullFullName),
            ttsTypeEnd = map.SourceType.IsTupleType ? TupleEnd : TypeEnd,
            sttTypeStart = map.TargetType.IsTupleType ? TupleStart : string.Format(TypeStart, map.TargetType.NotNullFullName),
            sttTypeEnd = map.TargetType.IsTupleType ? TupleEnd : TypeEnd,
            ttsSpacing = map.SourceType.IsTupleType ? " " : "\r\n            ",
            sttSpacing = map.TargetType.IsTupleType ? " " : "\r\n            ";

        MemberBuilder?
            sourceMemberMappers = null!;
        MemberBuilder?
            targetMemberMappers = null!;

        string?
            sttComma = null,
            ttsComma = null;

        var mapId = map.Id;

        bool
            toSameType = map.AreSameType,
            hasComplexTTSMembers = false,
            hasComplexSTTMembers = false,
            isTargetRecursive = false,
            isSourceRecursive = false,
            parentIgnoreTarget = ignore is ApplyOn.Target or ApplyOn.Both,
            parentIgnoreSource = ignore is ApplyOn.Source or ApplyOn.Both;

        map.BuildTargetValue ??= map.TargetHasScalarConversion || !source.Type.HasMembers
            ? (code, val) => code.Append(val)
            : (code, val) =>
            {
                code.Append(map.ToTargetMethodName).Append("(").Append(val);

                if (isTargetRecursive)
                {
                    code.Append(", depth + 1");

                    if (source.MaxDepth > 1)
                        code.Append(", ").Append(source.MaxDepth);
                }

                code.Append(")");
            };

        map.BuildTargetMethod = (StringBuilder code, ref RenderFlags isRendered) =>
        {
            if (map.IsTargetRendered)
                return;

            var maxDepth = target.MaxDepth;

            if (isTargetRecursive && target.MaxDepth == 0)
                maxDepth = target.MaxDepth = 1;

            isRendered.defaultMethod = true;

            if(target.Type.HasReachableZeroArgsCtor)
                CreateDefaultMethod(code, isTargetRecursive, maxDepth, sttTypeStart, sttTypeEnd, map.ToTargetMethodName, sourceExportFullTypeName, targetExportFullTypeName, source.DefaultBang, targetMemberMappers, hasComplexSTTMembers);

            if (MappingKind.Fill.HasFlag(map.MappingsKind))
            {
                isRendered.fillMethod = true;

                CreateFillMethod(code, isTargetRecursive, maxDepth, map.TargetType.IsValueType, map.FillTargetMethodName, targetExportFullTypeName, sourceExportFullTypeName, source.DefaultBang, targetMemberMappers, hasComplexSTTMembers);

                foreach (var item in map.TargetType.UnsafePropertyFieldsGetters)
                    item.Render(code);
            }

            if (map.AddTargetTryGet)
            {
                isRendered.tryGetMethod = true;

                CreateTryGetMethod(code, isTargetRecursive, maxDepth, map.TryGetTargetMethodName, map.ToTargetMethodName, sourceExportFullTypeName, targetExportFullTypeName, source.DefaultBang, targetMemberMappers, hasComplexSTTMembers);
            }
        };

        map.BuildSourceValue ??= map.TargetHasScalarConversion || !target.Type.HasMembers
            ? (code, val) => code.Append(val)
            : (code, val) =>
            {
                code.Append(map.ToSourceMethodName).Append("(").Append(val);

                if (isSourceRecursive)
                {
                    code.Append(", depth + 1");

                    if (target.MaxDepth > 1)
                        code.Append(", ").Append(target.MaxDepth);
                }
                code.Append(")");
            };

        map.BuildSourceMethod = (StringBuilder code, ref RenderFlags isRendered) =>
        {
            if (map.IsSourceRendered)
                return;

            if (isSourceRecursive && source.MaxDepth == 0)
                source.MaxDepth = 1;

            isRendered.defaultMethod = true;

            if (source.Type.HasReachableZeroArgsCtor)
                CreateDefaultMethod(code, isSourceRecursive, source.MaxDepth, ttsTypeStart, ttsTypeEnd, map.ToSourceMethodName, targetExportFullTypeName, sourceExportFullTypeName, target.DefaultBang, sourceMemberMappers, hasComplexTTSMembers);

            if (MappingKind.Fill.HasFlag(map.MappingsKind))
            {
                isRendered.fillMethod = true;

                CreateFillMethod(code, isSourceRecursive, source.MaxDepth, map.SourceType.IsValueType, map.FillSourceMethodName, sourceExportFullTypeName, targetExportFullTypeName, target.DefaultBang, sourceMemberMappers, hasComplexTTSMembers);

                foreach (var item in map.SourceType.UnsafePropertyFieldsGetters)
                    item.Render(code);
            }

            if (map.AddSourceTryGet)
            {
                isRendered.tryGetMethod = true;

                CreateTryGetMethod(code, isSourceRecursive, source.MaxDepth, map.TryGetSourceMethodName, map.ToSourceMethodName, targetExportFullTypeName, sourceExportFullTypeName, target.DefaultBang, sourceMemberMappers, hasComplexTTSMembers);
            }

            map.BuildSourceMethod = null;
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
                        map.TargetMemberCount++;

                        if (targetMember.IsNullable)
                            map.AddTargetTryGet = true;

                        map.TargetType.IsRecursive = isTargetRecursive = true;

                        hasComplexSTTMembers = true;
                        
                        targetMemberMappers += (code, isFill) =>
                        {
                            BuildMemberMapping(
                                code,
                                isFill,
                                ref sttComma,
                                sttSpacing,
                                map.BuildTargetValue,
                                sourceMember,
                                targetMember,
                                map.ToTargetMethodName,
                                map.FillTargetMethodName,
                                map.SourceRequiresMapper,
                                map.SourceHasScalarConversion);

                            if (targetMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += code => nullUnsafeAccesor.Render(code);
                        };
                    }

                    if (!(toSameType && parentIgnoreSource || ignoreSource || map.SourceType.IsInterface))
                    {
                        map.SourceMemberCount++;

                        map.SourceType.IsRecursive = isSourceRecursive = true;

                        hasComplexTTSMembers = true;

                        if (sourceMember.IsNullable)
                            map.AddSourceTryGet = true;
                        
                        sourceMemberMappers += (code, isFill) =>
                        {
                            BuildMemberMapping(
                                code,
                                isFill,
                                ref ttsComma,
                                ttsSpacing,
                                map.BuildSourceValue,
                                targetMember,
                                sourceMember,
                                map.ToSourceMethodName,
                                map.FillSourceMethodName,
                                map.TargetRequiresMapper,
                                map.TargetHasScalarConversion);

                            if (sourceMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += code => nullUnsafeAccesor.Render(code);
                        };
                    }

                    map.CanMap |= map.HasTargetToSourceMap || map.HasTargetToSourceMap;

                    break;
                }

                var memberMap = ParseTypesMap(
                    GetOrAddMapper(targetMember.Type, sourceMember.Type),
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
                        map.TargetMemberCount++;

                        if (sourceMember.IsNullable)
                            memberMap.AddTargetTryGet = true;

                        hasComplexSTTMembers = !memberMap.TargetType.IsPrimitive;

                        map.TargetType.IsRecursive |=
                            isTargetRecursive |=
                            memberMap.TargetType.IsRecursive |=
                            memberMap.IsCollection is true && memberMap.TargetType.CollectionInfo.ItemDataType.Id == target.Type.Id;

                        targetMemberMappers += (code, isFill) =>
                        {
                            BuildMemberMapping(
                                code,
                                isFill,
                                ref sttComma,
                                sttSpacing,
                                memberMap.BuildTargetValue,
                                sourceMember,
                                targetMember,
                                memberMap.ToTargetMethodName,
                                memberMap.FillTargetMethodName,
                                memberMap.SourceRequiresMapper,
                                memberMap.SourceHasScalarConversion);

                            if (targetMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += code => nullUnsafeAccesor.Render(code);
                        };
                    }

                    if (!(toSameType || parentIgnoreSource || ignoreSource || map.SourceType.IsInterface))
                    {
                        map.SourceMemberCount++;

                        if (targetMember.IsNullable)
                            memberMap.AddSourceTryGet = true;

                        hasComplexTTSMembers = !memberMap.SourceType.IsPrimitive;

                        map.SourceType.IsRecursive |=
                            isSourceRecursive |=
                            memberMap.SourceType.IsRecursive |=
                            memberMap.IsCollection is true && memberMap.SourceType.CollectionInfo.ItemDataType.Id == source.Type.Id;

                        sourceMemberMappers += (code, isFill) =>
                        {
                            BuildMemberMapping(
                                code,
                                isFill,
                                ref ttsComma,
                                ttsSpacing,
                                memberMap.BuildSourceValue,
                                targetMember,
                                sourceMember,
                                memberMap.ToSourceMethodName,
                                memberMap.FillSourceMethodName,
                                memberMap.TargetRequiresMapper,
                                memberMap.TargetHasScalarConversion);

                            if (sourceMember.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
                                map.AuxiliarMappings += code => nullUnsafeAccesor.Render(code);
                        };
                    }

                    if (map.MappingsKind == MappingKind.All && memberMap.MappingsKind != MappingKind.All
                        && (sourceMember.IsInit || targetMember.IsInit))
                    {
                        memberMap.MappingsKind = MappingKind.All;
                    }

                    if (true == (map.CanMap |= map.HasTargetToSourceMap || map.HasSourceToTargetMap)
                        && (!memberMap.TargetType.IsPrimitive || memberMap.TargetType.IsTupleType
                        || !memberMap.SourceType.IsPrimitive || memberMap.SourceType.IsTupleType))
                    {
                        map.AuxiliarMappings += memberMap.BuildMethods;
                    }

                    break;
                }
            }
        }

        map.TargetRequiresMapper = map.TargetMemberCount > 0;
        map.SourceRequiresMapper = map.SourceMemberCount > 0;

        if (!map.HasSourceToTargetMap)
        {
            map.AddTargetTryGet = false;
        }

        if (!map.HasTargetToSourceMap)
        {
            map.AddSourceTryGet = false;
        }

        return;

        void CreateDefaultMethod(
            StringBuilder code,
            bool isRecursive,
            int maxDepth,
            string typeStart,
            string typeEnd,
            string methodName,
            string sourceFullTypeName,
            string targetFullTypeName,
            string? defaultSourceBang,
            MemberBuilder? members,
            bool hasComplexMembers)
        {
            if (members is null) return;
            
            string 
                targetExportFullXmlDocTypeName = targetExportFullTypeName.Replace("<", "{").Replace(">", "}"),
                sourceExportFullXmlDocTypeName = sourceExportFullTypeName.Replace("<", "{").Replace(">", "}");


            code.Append(@"
    /// <summary>
    /// Creates a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based on the given instance of <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/>
    /// </summary>
    /// <param name=""source"">Data source to be mapped</param>");

            code.Append(isRecursive
                ? @"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>"
                : @"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");

            code.Append(@"
    public static ").Append(targetFullTypeName).AddSpace().Append(methodName).Append("(this ").Append(sourceFullTypeName).Append(" source");

            if (isRecursive)
            {
                code.Append(@", int depth = 0, int maxDepth = ").Append(maxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return default").Append(defaultSourceBang).Append(@";
");
            }
            else
            {
                code.Append(@")
    {");
            }
            
            code.Append(@"
        return ").Append(typeStart);


            members(code, false);

            code.Append(typeEnd).Append(@";
    }
");
        }

        void CreateTryGetMethod
        (
            StringBuilder code,
            bool isRecursive,
            short maxDepth,
            string methodName,
            string toMethodName,
            string sourceFullTypeName,
            string targetFullTypeName,
            string? defaultSourceBang,
            MemberBuilder? members,
            bool hasComplexMembers
        )
        {
            if (members is null) return;

            string
                targetExportFullXmlDocTypeName = targetExportFullTypeName.Replace("<", "{").Replace(">", "}"),
                sourceExportFullXmlDocTypeName = sourceExportFullTypeName.Replace("<", "{").Replace(">", "}");

            code.Append(@"
    /// <summary>
    /// Tries to create a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based on a given instance of <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/> if it's not null
    /// </summary>
    /// <param name=""source"">Source instance</param>
    /// <param name=""target"">Target instance</param>")
                .Append(isRecursive
                ? @"
    /// <param name=""depth"">Depth index for recursive control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>"
                : @"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]")
                .Append(@"
    public static bool ").Append(methodName).Append("(this ").Append(sourceFullTypeName).Append(" source, out ").Append(targetFullTypeName).Append(" target");

            if (isRecursive)
            {
                code.Append(", int depth = 0, int maxDepth = ").Append(maxDepth).Append(@")
    {");
            }
            else
            {
                code.Append(@")
    {");
            }

            code.Append(@"
        if (source is { } _source)
        {
            target = ").Append(toMethodName).Append("(_source");

            if (isRecursive)
            {
                code.Append(", depth, maxDepth");
            }

            code.Append(@");
            return true;
        }
        target = default").Append(defaultSourceBang).Append(@";
        return false;
    }
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
            MemberBuilder? members,
            bool hasComplexMembers
        )
        {
            if (members is null) return;

            var _ref = (isValueType ? "ref " : null);

            string
                targetExportFullXmlDocTypeName = targetExportFullTypeName.Replace("<", "{").Replace(">", "}"),
                sourceExportFullXmlDocTypeName = sourceExportFullTypeName.Replace("<", "{").Replace(">", "}");

            code.Append(@"
    /// <summary>
    /// Update an instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based on a given instance of <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@""" />
    /// </summary>
    /// <param name=""source"">Source instance</param>
    /// <param name=""target"">Target instance</param>");

            if (isRecursive)
            {
                code.Append(@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");
            }
            else
            {
                code.Append(@"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            }

            code.Append(@"
    public static ").Append(_ref).Append(targetFullTypeName).AddSpace().Append(fillMethodName).Append("(").Append(_ref).Append("this ").Append(targetFullTypeName).Append(" target, ").Append(sourceFullTypeName).Append(" source");

            if (isRecursive)
            {
                code.Append(", int depth = 0, int maxDepth = ").Append(maxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return ").Append(_ref).Append("target").Append(sourceBang).Append(";");
            }
            else
            {
                code.Append(@")
    {");
            }

            members(code, true);

            code.Append(@"

        return ").Append(_ref).Append(@"target;
    }
");
        }
    }
}
