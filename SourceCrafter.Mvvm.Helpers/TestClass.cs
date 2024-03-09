using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Binding.Attributes;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Test;
file enum MapperType { Method, Properties, Enumerables }

public class TestClass
{
    //readonly SymbolsMap map = new();
    static readonly EqualityComparer<uint> _uintComparer = EqualityComparer<uint>.Default;
    internal static readonly SymbolEqualityComparer _comparer = SymbolEqualityComparer.IncludeNullability;

    [Fact]
    internal void TestCodeGenerator()
    {
        Initialize(0);

        int leftScopeId = 0, rightScopeId = 0;

        GetRootAndModel(@"using SourceCrafter.Binding.Attributes;
[assembly: Bind<User?, UserDto>(ignoreMembers: [nameof(User.Assistance)])]
", out CSharpCompilation compilation, out var root, out var model, typeof(BindAttribute<,>), typeof(UserDto));

        TypeInfo.objectTypeSymbol = new(compilation.GetTypeByMetadataName("System.Object")!);

        StringBuilder code = new(@"namespace SourceCrafter.Mappings;

public static partial class Mappers
{");

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (attr.AttributeClass?.ToGlobalizedNonGenericNamespace() == "global::SourceCrafter.Binding.Attributes.BindAttribute")
            {
                //Indent indent = new();

                ITypeSymbol
                    leftType = attr.AttributeClass.TypeArguments[0],
                    rightType = attr.AttributeClass.TypeArguments[1];

                Member
                    left = new(-1, "to", new(leftType), leftType.IsNullable()),
                    right = new(1, "source", new(rightType), rightType.IsNullable());

                GetNullability(ref left, ref right);

                if (GetOrAddMapper(++leftScopeId, --rightScopeId, left, right) is { CanMap: true } existingMap)
                {
                    if (existingMap.HasLeftToRightMap)
                    {
                        string scopeId = $"+{leftScopeId}+";

                        if (right.IsNullable)
                            CreateNullableMethod(scopeId, left, right, existingMap.Bootstrap, existingMap.TypeBuilder);
                        else
                            CreateMethod(scopeId, left, right, existingMap.Bootstrap, existingMap.TypeBuilder);
                    }
                    if (existingMap.HasRightToLeftMap)
                    {
                        string scopeId = $"+{rightScopeId}+";

                        if (left.IsNullable)
                            CreateNullableMethod(scopeId, right, left, existingMap.ReverseBootstrap, existingMap.ReverseTypeBuilder);
                        else
                            CreateMethod(scopeId, right, left, existingMap.ReverseBootstrap, existingMap.ReverseTypeBuilder);
                    }
                }
            }
        }

        code.Append(@"
}");

        void CreateNullableMethod(string scopeId, Member left, Member right, CodeBuilder? initialize, CodeBuilder createType)
        {
            string? comma = null;
            Indent indent = new();

            code.AppendFormat(@"
    public static bool TryGet{0}(this {1} source, out {2} output)
    {{
        if(source is null)
        {{
            output = default{3};
            return false;
        }}",
                SanitizeTypeName(left.Type._typeSymbol),
                right.Type.FullName,
                left.Type.FullName.TrimEnd('?'),
                right.DefaultBang);

            initialize?.Invoke(scopeId, "output", "source", indent, ref comma);

            code.Append(@"
        
        output = ");

            createType(scopeId, "output", "source", indent, ref comma);

            code.Append(@";

        return true;
    }");
        }

        void CreateMethod(string scopeId, Member left, Member right, CodeBuilder? initialize, CodeBuilder createType)
        {
            string? comma = null;
            Indent indent = new();

            code.AppendFormat(@"
    public static {1} As{0}(this {2} source)
    {{",
                SanitizeTypeName(left.Type._typeSymbol),
                left.Type.FullName.TrimEnd('?'),
                right.Type.FullName.TrimEnd('?'));

            initialize?.Invoke(scopeId, "output", "source", indent, ref comma);

            code.Append(@"

        return ");

            createType!(scopeId, "output", "source", indent, ref comma);

            code.Append(@";
    }");
        }

        ref TypeMapping GetOrAddMapper(int leftScopeid, int rightScopeid, Member left, Member right)
        {
            if (_buckets == null)
            {
                Initialize(0);
            }


            var entries = _entries;

            int leftId = left.Type.Id,
                rightId = right.Type.Id;

            var hashCode = GetId(leftId, rightId);

            uint collisionCount = 0;
            ref int bucket = ref GetBucket(hashCode);
            int i = bucket - 1; // Value in _buckets is 1-based


            while (true)
            {
                // Should be a while loop https://github.com/dotnet/runtime/issues/9422
                // Test uint in if rather than loop condition to drop range check for following array access
                if ((uint)i >= (uint)entries.Length)
                {
                    break;
                }

                if (_uintComparer.Equals(entries[i].Id, hashCode))
                {
                    return ref entries[i]!;
                }

                i = entries[i].next;

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

            entries[index] = new(hashCode, left.Type, right.Type, bucket - 1);

            ref var entry = ref entries[index];

            entry.next = bucket - 1; // Value in _buckets is 1-based

            bucket = index + 1; // Value in _buckets is 1-based

            _version++;

            CanCreateType(leftScopeid, rightScopeid, left, right, ref entry);

            return ref entry;
        }

        void CanCreateType(int leftScopeId, int rightScopeId, Member left, Member right, ref TypeMapping mapping, bool buildType = true)
        {
            if (true == mapping.CanMap)
            {
                return;
            }
            else if (mapping.IsScalar)
            {
                var conversion = compilation.ClassifyConversion(left.Type._typeSymbol, right.Type._typeSymbol);

                if (conversion.Exists)
                {
                    mapping.TypeBuilder = BuildScalar(left, right, conversion, buildType);

                    mapping.HasLeftToRightMap = true;
                }
                else
                {
                    mapping.HasLeftToRightMap = false;
                }

                conversion = compilation.ClassifyConversion(right.Type._typeSymbol, left.Type._typeSymbol);

                if (conversion.Exists)
                {
                    mapping.ReverseTypeBuilder = BuildScalar(right, left, conversion, buildType);

                    mapping.HasRightToLeftMap = true;
                }
                else
                {
                    mapping.HasRightToLeftMap = false;
                }

                mapping.CanMap = mapping.HasLeftToRightMap || mapping.HasRightToLeftMap;
            }
            else if (mapping.IsCollection)
            {
                TypeInfo
                    leftItemType = left.Type.ItemType!,
                    rightItemType = right.Type.ItemType!;

                Member
                    leftItem = new(left.Id, left.Name + "Item", leftItemType, left.Type.IsItemTypeNullable),
                    rightItem = new(right.Id, right.Name + "Item", rightItemType, right.Type.IsItemTypeNullable);

                CodeBuilder
                    propsBuilder = null!,
                    reversePropsBuilder = null!;
                CodeBuilder?
                    initializerBuilder = null!,
                    reverseInitializerBuilder = null!;

                mapping.TypeBuilder = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    propsBuilder(path, leftPart, rightPart, indent, ref comma);

                mapping.ReverseTypeBuilder = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    reversePropsBuilder(path, leftPart, rightPart, indent, ref comma);

                mapping.Bootstrap = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    initializerBuilder(path, leftPart, rightPart, indent, ref comma);

                mapping.ReverseBootstrap = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    reverseInitializerBuilder(path, leftPart, rightPart, indent, ref comma);

                ref var itemMapping = ref GetOrAddMapper(leftScopeId, rightScopeId, leftItem, rightItem);

                if (itemMapping.CanMap != false)
                {
                    bool isClass = !itemMapping.IsScalar;

                    if (mapping.HasLeftToRightMap = BuildCollection(
                        left,
                        right,
                        leftItem,
                        rightItem,
                        isClass,
                        ref itemMapping.Bootstrap,
                        itemMapping.TypeBuilder,
                        (left.Type.IterType, right.Type.IterType),
                        buildType,
                        ref initializerBuilder,
                        ref propsBuilder))
                    {
                        itemMapping.Bootstrap += mapping.Bootstrap;
                    }


                    if (mapping.HasRightToLeftMap = BuildCollection(
                        right,
                        left,
                        rightItem,
                        leftItem,
                        isClass,
                        ref itemMapping.ReverseBootstrap,
                        itemMapping.ReverseTypeBuilder,
                        (right.Type.IterType, left.Type.IterType),
                        buildType,
                        ref reverseInitializerBuilder,
                        ref reversePropsBuilder))
                    {
                        itemMapping.ReverseBootstrap += mapping.ReverseBootstrap;
                    }

                    mapping.CanMap = mapping.HasLeftToRightMap || mapping.HasRightToLeftMap;
                }
                else
                {
                    mapping.CanMap = false;
                }
            }
            else if (null == mapping.CanMap)
            {
                int l = -1, r = -1;
                uint mapId = mapping.Id;

                ImmutableArray<ISymbol>
                    leftMembers = mapping.LeftMembers,
                    rightMembers = mapping.RightMembers;

                int lLen = leftMembers.Length, rLen = rightMembers.Length;

                CodeBuilder
                    propsBuilder = null!,
                    reversePropsBuilder = null!;

                mapping.TypeBuilder = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                {
                    comma = null;

                    code.AppendFormat(@"new {0}
{1}    {{", left.Type.FullName.TrimEnd('?'), indent);

                    indent++;

                    propsBuilder($"{path}{left.Id}+", leftPart, rightPart, indent, ref comma);

                    indent--;

                    code.AppendFormat(@"
{0}    }}", indent);
                };

                mapping.ReverseTypeBuilder = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                {
                    comma = null;

                    code.AppendFormat(@"new {0}
{1}    {{", right.Type.FullName.TrimEnd('?'), indent);

                    indent++;

                    reversePropsBuilder($"{path}{right.Id}+", leftPart, rightPart, indent, ref comma);

                    indent--;

                    code.AppendFormat(@"
{0}    }}", indent);
                };

                while (++l < lLen)
                {
                    if (IsNotMappable(leftMembers[l], out var leftMember))
                        continue;

                    while (++r < rLen)
                    {
                        if (IsNotMappable(rightMembers[r], out var rightMember)
                            || !AreMappableByDesign(ref leftMember, ref rightMember, out var canWriteLeft, out var canWriteRight))
                            continue;

                        GetNullability(ref leftMember, ref rightMember);

                        if (mapId == GetId(leftMember.Type.Id, rightMember.Type.Id))
                        {
                            if (mapping.HasLeftToRightMap || mapping.CanMap == null)
                            {
                                propsBuilder += BuildTypeMember(leftScopeId, mapping.TypeBuilder, leftMember, rightMember, mapping.CanDepth);
                                mapping.HasLeftToRightMap = true;
                            }

                            if (mapping.HasRightToLeftMap || mapping.CanMap == null)
                            {
                                reversePropsBuilder += BuildTypeMember(rightScopeId, mapping.ReverseTypeBuilder, rightMember, leftMember, mapping.CanDepth);
                                mapping.HasRightToLeftMap = true;
                            }

                            break;
                        }

                        ref var memberMapping = ref GetOrAddMapper(leftScopeId, rightScopeId, leftMember, rightMember);

                        if (memberMapping is { CanMap: not false })
                        {
                            if (canWriteLeft)
                            {
                                if (memberMapping.IsCollection)
                                    mapping.Bootstrap += memberMapping.Bootstrap;

                                propsBuilder += BuildTypeMember(leftScopeId, memberMapping.TypeBuilder, leftMember, rightMember, memberMapping.CanDepth);

                                mapping.CanMap = mapping.HasLeftToRightMap = true;
                            }
                            if (canWriteRight)
                            {
                                if (memberMapping.IsCollection)
                                    mapping.ReverseBootstrap += memberMapping.ReverseBootstrap;

                                reversePropsBuilder += BuildTypeMember(rightScopeId, memberMapping.ReverseTypeBuilder, rightMember, leftMember, memberMapping.CanDepth);

                                mapping.CanMap = mapping.HasRightToLeftMap = true;
                            }

                            break;
                        }
                        else
                        {
                            mapping.CanMap = false;
                            break;
                        }
                    }
                    r = -1;
                }

                CodeBuilder BuildTypeMember(int scopeId, CodeBuilder createType, Member leftMember, Member rightMember, bool canDepth)
                {
                    bool 
                        checkNull = !leftMember.IsNullable && (rightMember.IsNullable || (rightMember.Type is {AllowsNull: true, Name: not ("string" or "object") })),
                        isIterable = leftMember.Type.IsIterable,
                        isMandatory = leftMember.IsMandatory;

                    string 
                        leftName = leftMember.Name,
                        rightName = rightMember.Name;

                    return (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    {
                        string key = $"{path}{leftMember.Id}+";

                        if (!canDepth || !leftMember.MaxDepthReached(key))
                        {
                            code.AppendFormat(@"{0}
{1}    {2} = ", Exchange(ref comma, ","), indent, leftName);

                            if (isIterable)
                                rightPart = leftPart + leftName;
                            else
                            {
                                if (!checkNull)
                                    rightPart += ".";

                                rightPart += rightName;
                            }

                            if (isIterable || !checkNull )
                            {
                                createType(key, leftName, rightPart , indent, ref comma);
                            }
                            else
                            {
                                code.AppendFormat(@"{0}.{1} is {{}} {0}{1}
{2}        ? ", rightPart, rightName, indent);

                                indent++;

                                createType(key, leftName, rightPart, indent, ref comma);

                                indent--;

                                code.AppendFormat(@"
{0}        : default{1}", indent, rightMember.DefaultBang);
                            }
                        }
                        else if(isMandatory)
                        {
                            code.AppendFormat(@"{0}
{1}    {2} = default{3}", Exchange(ref comma, ","), indent, leftName, rightMember.DefaultBang);
                        }
                    };
                }
            }
        }

        CodeBuilder BuildScalar(Member leftMember, Member rightMember, Conversion conversion, bool creatingType)
        {
            return (conversion.IsExplicit, rightMember.IsNullable) switch
            {
                (true, true) =>
                    (string path, string left, string right, Indent indent, ref string? comma)
                        => _ = right.Contains('.') || right.Contains('[')
                            ? code.AppendFormat(@"{0} is {{}} {1} ? ({2}){1} : default{3}", right, SanitizeScalar(right), leftMember.Type.FullName, rightMember.Bang)
                            : code.AppendFormat(@"{0} is {{}} ? ({1}){0} : default{2}", right, leftMember.Type.FullName, rightMember.Bang),

                (true, false) =>
                    (string path, string left, string right, Indent indent, ref string? comma)
                        => code.AppendFormat(@"({0}){1}{2}", leftMember.Type.FullName, right, rightMember.Bang),

                (false, true) =>
                    (string path, string left, string right, Indent indent, ref string? comma)
                        => code.AppendFormat(@"{0} is {{}} ? {0} : default{1}", right, rightMember.DefaultBang),

                _ =>
                    (string path, string left, string right, Indent indent, ref string? comma)
                        => code.AppendFormat(@"{0}{1}", right, rightMember.Bang)
            };
        }

        static bool IsNotMappable(ISymbol member, out Member memberOut)
        {
            return (memberOut = member switch
            {
                IPropertySymbol
                {
                    ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                    IsIndexer: false,
                    Type: { } type,
                    DeclaredAccessibility: var accessibility,
                    IsReadOnly: var isReadonly,
                    IsWriteOnly: var isWriteOnly
                } prop
                    => new(_comparer.GetHashCode(member),
                            member.ToNameOnly(),
                            new(type),
                            type.IsNullable(),
                            accessibility is Accessibility.Internal or Accessibility.Public && !isWriteOnly,
                            accessibility is Accessibility.Internal or Accessibility.Public && !isReadonly,
                            member.GetAttributes(),
                            prop.SetMethod?.IsInitOnly == true),
                IFieldSymbol
                {
                    ContainingType.Name: not ("IEnumerator" or "IEnumerator<T>"),
                    Type: { } type,
                    DeclaredAccessibility: var accessibility,
                    IsReadOnly: var isReadonly
                }
                    => new(TypeInfo.GetHashCode(member),
                            member.ToNameOnly(),
                            new(type),
                            type.IsNullable(),
                            accessibility is Accessibility.Internal or Accessibility.Public,
                            accessibility is Accessibility.Internal or Accessibility.Public && !isReadonly,
                            member.GetAttributes()),
                _
                    => default,
            }).Exists == false;
        }

        bool AreMappableByDesign(ref Member left, ref Member right, out bool canWriteLeft, out bool canWriteRight)
        {
            return (canWriteLeft = canWriteRight = left.Name == right.Name)
                | CheckMappability(ref canWriteLeft, ref left, right)
                | CheckMappability(ref canWriteRight, ref right, left);
        }

        bool CheckMappability(ref bool canWrite, ref Member left, Member right)
        {
            if (!left.IsWritable || !right.IsReadable)
                return canWrite = false;

            Ignore ignore;

            foreach (var attr in left.Attributes)
            {
                if (attr.AttributeClass?.ToDisplayString() is not { } className) continue;

                if (className == "SourceCrafter.Mapping.Attributes.IgnoreAttribute")
                {
                    if ((ignore = (Ignore)(int)attr.ConstructorArguments[0].Value!) is Ignore.This or Ignore.Both)
                        return false;

                    left.IgnoresRight = ignore is Ignore.This or Ignore.Both;
                }

                if (className == "SourceCrafter.Mapping.Attributes.MaxDepthAttribute")
                {
                    left.MaxDepth = (byte)attr.ConstructorArguments[1].Value!;
                    continue;
                }

                if (className != "SourceCrafter.Mapping.Attributes.MapAttribute")
                    continue;

                if ((ignore = (Ignore)(int)attr.ConstructorArguments[0].Value!) is Ignore.This or Ignore.Both)
                    return false;

                left.IgnoresRight = ignore is Ignore.This or Ignore.Both;

                canWrite |= (attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0].Expression
                    is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                }
                    && _comparer.GetHashCode(compilation.GetSemanticModel(id.SyntaxTree).GetSymbolInfo(id).Symbol) == right.Id;
            }

            return canWrite;
        }

        static string SanitizeTypeName(ITypeSymbol type)
        {
            string typeName = type.ToTypeNameFormat();

            return type switch
            {
                INamedTypeSymbol { IsGenericType: true } =>
                    typeName.Replace("<", "Of").Replace(">", "_").Replace(",", "And").Replace(" ", "").TrimEnd('_', '?'),
                INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els } =>
                    "TupleOf" + string.Join("And", els.Select(f => f.IsExplicitlyNamedTupleElement ? f.Name : SanitizeTypeName(f.Type))),
                _ =>
                    typeName.TrimEnd('?'),
            };
        }

        static void GetNullability(ref Member left, ref Member right)
        {
            right.DefaultBang = GetDefaultBangChar(left.IsNullable, right.IsNullable, right.Type.AllowsNull);
            right.Bang = GetBangChar(left.IsNullable, right.IsNullable);
            left.DefaultBang = GetDefaultBangChar(right.IsNullable, left.IsNullable, left.Type.AllowsNull);
            left.Bang = GetBangChar(right.IsNullable, left.IsNullable);
        }

        bool BuildCollection(Member leftIter, Member rightIter, Member leftItem, Member rightItem, bool isClass, ref CodeBuilder? bootstrapItem, CodeBuilder createItemType, (IterableType, IterableType) toFrom, bool buildType, ref CodeBuilder? bootstrap, ref CodeBuilder createType)
        {
            var itemBootstrap = bootstrapItem;

            switch (toFrom)
            {
                case (not IterableType.Collection, not IterableType.Enumerable):

                    createType = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    {
                        code.Append(rightPart + "Temp");
                    };

                    bootstrap = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    {
                        path += leftIter.Id + "+";
                        if (leftIter.MaxDepthReached(path))
                        {
                            return;
                        }

                        code.AppendFormat(@"
{0}    #region Translating from {1}.{2} to {3}.{4}

{0}    var {3}{4}Temp = global::System.Array.Empty<{5}>();
", indent, rightPart, rightIter.Name, leftPart, leftIter.Name, leftItem.Type.FullName);

                        if (!leftIter.IsNullable && rightIter.IsNullable)
                        {
                            code.AppendFormat(@"
{0}    if({1}.{2} is not null)
{0}    {{", indent, rightPart, rightIter.Name);
                            indent++;
                        }

                        code.AppendFormat(@"
{0}    var {1}Temp = {2}.{3};
{0}    var {1}Len = {1}Temp.{4};

{0}    for(int {1}Ix = 0; {1}Ix < {1}Len; {1}Ix++) 
{0}    {{", indent, rightPart + rightIter.Name, rightPart, rightIter.Name, rightIter.Type.CountProperty);

                        indent++;

                        if (itemBootstrap != null)
                        {
                            code.AppendFormat(@"
{0}    var {1}TempItem = {1}Temp[{1}Ix];", indent, rightPart + rightIter.Name);

                            string
                                leftItemName = $"{leftPart}{leftIter.Name}TempItem",
                                rightItemName = $"{rightPart}{rightIter.Name}TempItem";
                            itemBootstrap(path, leftItemName, rightItemName, indent, ref comma);

                            code.AppendFormat(@"
{0}    {1}Temp[{1}Ix] = ", indent, leftPart + leftIter.Name);

                            createItemType(path, leftItemName, rightItemName, indent, ref comma);

                        }
                        else
                        {
                            string
                                leftItemName = $"{leftPart}{leftIter.Name}Temp[{rightPart}{rightIter.Name}Ix]",
                                rightItemName = $"{rightPart}{rightIter.Name}Temp[{rightPart}{rightIter.Name}Ix]";

                            code.AppendFormat(@"
{0}    {1}Temp[{2}Ix] = ", indent, leftPart + leftIter.Name, rightPart + rightIter.Name);

                            createItemType(path, leftItemName, rightItemName, indent, ref comma);

                        }

                        indent--;

                        code.AppendFormat(@";
{0}    }}", indent);

                        if (rightIter.IsNullable)
                        {
                            indent--;
                            code.AppendFormat(@"
{0}    }}", indent);
                        }
                        code.AppendFormat(@"

{0}    #endregion", indent);
                    };

                    break;

                case ((IterableType.Collection or IterableType.Enumerable) and { } type, _):

                    createType = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    {
                        code.Append(rightPart + "Temp");
                    };

                    bootstrap = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    {
                        path += leftIter.Id + "+";

                        if (leftIter.MaxDepthReached(path))
                        {
                            return;
                        }

                        var (collectionType, addMethod) = type == IterableType.Enumerable
                            ? ("global::System.Collections.Generic.List<" + leftIter.Type.ItemType!.FullName + ">", "Add")
                            : (leftIter.Type.FullName.TrimEnd('?'), leftIter.Type.AddMethod);

                        code.AppendFormat(@"
{0}    #region Translating from {1}.{2} to {3}.{4}

{0}    var {3}{4}Temp = new {5}();
", indent, rightPart, rightIter.Name, leftPart, leftIter.Name, collectionType);

                        if (!leftIter.IsNullable && rightIter.IsNullable)
                        {
                            code.AppendFormat(@"
{0}    if({1}.{2} is not null)
{0}    {{", indent, rightPart, rightIter.Name);
                            indent++;
                        }

                        code.AppendFormat(@"
{0}    foreach (var {1}TempItem in {2}.{3}) 
{0}    {{", indent, rightPart + rightIter.Name, rightPart, rightIter.Name);

                        indent++;

                        string
                            leftItemName = $"{leftPart}{leftIter.Name}TempItem",
                            rightItemName = $"{rightPart}{rightIter.Name}TempItem";

                        itemBootstrap?.Invoke(path, leftItemName, rightItemName, indent, ref comma);

                        code.AppendFormat(@"
{0}    {1}Temp.{2}(", indent, leftPart + leftIter.Name, addMethod);

                        createItemType(path, leftItemName, rightItemName, indent, ref comma);

                        indent--;

                        code.AppendFormat(@");
{0}    }}", indent);

                        if (rightIter.IsNullable)
                        {
                            indent--;
                            code.AppendFormat(@"
{0}    }}", indent);
                        }
                        code.AppendFormat(@"

{0}    #endregion", indent);
                    };

                    break;
                //case (IterableType.Enumerable, _):
                //    break;
                case (IterableType.Array or IterableType.Enumerable, _):


                    createType = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    {
                        code.Append(rightPart + "Temp");
                    };
                    bootstrap = (string path, string leftPart, string rightPart, Indent indent, ref string? comma) =>
                    {
                        path += leftIter.Id + "+";
                        if (leftIter.MaxDepthReached(path))
                        {
                            return;
                        }

                        code.AppendFormat(@"
{0}    #region Translating from {1}.{2} to {3}.{4}

{0}    {5} {3}{4}Temp = new {6}[4];
", indent, rightPart, rightIter.Name, leftPart, leftIter.Name, leftIter.Type.FullName.TrimEnd('?'), leftItem.Type.FullName);

                        if (rightIter.IsNullable)
                        {
                            code.AppendFormat(@"
{0}    if({1}.{2} is not null)
{0}    {{", indent, rightPart, rightIter.Name);
                            indent++;
                        }

                        code.AppendFormat(@"
{0}    int {1}TempLen = 0, {1}Ix = 0;

{0}    foreach (var {2}TempItem in {3}.{4})
{0}    {{
{0}        if ({1}TempLen == {1}Temp.Length) 
{0}            global::System.Array.Resize(ref {1}Temp, {1}Temp.Length * 2);
", indent, leftPart + leftIter.Name, rightPart + rightIter.Name, rightPart, rightIter.Name);

                        indent++;

                        string
                            leftItemName = $"{leftPart}{leftIter.Name}TempItem",
                            rightItemName = $"{rightPart}{rightIter.Name}TempItem";

                        itemBootstrap?.Invoke(path, leftItemName, rightItemName, indent, ref comma);

                        code.AppendFormat(@"
{0}    {1}Temp[{1}Ix] = ", indent, leftPart + leftIter.Name);

                        createItemType(path, leftItemName, rightItemName, indent, ref comma);

                        indent--;

                        code.AppendFormat(@";
{0}    }}", indent);

                        if (rightIter.IsNullable)
                        {
                            indent--;
                            code.AppendFormat(@"
{0}    }}", indent);
                        }
                        code.AppendFormat(@"

{0}    #endregion", indent);
                    };
                    break;
                default:
                    return false;
            }

            itemBootstrap += bootstrap;

            return true;
        }
    }

    private static string SanitizeScalar(string memberName)
    {
        return memberName.LastIndexOf('[') is not -1 and int r
            ? memberName[..r] + "Item"
            : memberName.Replace(".", "");

    }

    static uint GetId(int leftId, int rightId)
        => (uint)(Math.Min(leftId, rightId), Math.Max(leftId, rightId)).GetHashCode();

    static string? GetDefaultBangChar(bool isLeftNullable, bool isRightNullable, bool rightAllowsNull)
        => !isLeftNullable && (rightAllowsNull || isRightNullable) ? "!" : null;

    static string? GetBangChar(bool isLeftNullable, bool isRightNullable)
        => !isLeftNullable && isRightNullable ? "!" : null;

    [DebuggerNonUserCode]
    [DebuggerStepThrough]
    static void GetRootAndModel(string code, out CSharpCompilation compilation, out SyntaxNode root, out SemanticModel model, params Type[] assemblies)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);

        root = tree.GetRoot();

        compilation = CSharpCompilation
            .Create(
                "Temp",
                new[] { tree },
                assemblies
                    .Select(a => a.Assembly.Location)
                    .Distinct()
                    .Append(typeof(object).Assembly.Location)
                    .Select(l => MetadataReference.CreateFromFile(l))
                    .ToArray()
            );

        model = compilation.GetSemanticModel(tree);
    }

    static string? Exchange(ref string? init, string? update = null) => ((init, update) = (update, init)).update;

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

    private uint Initialize(uint capacity)
    {
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
    private static uint FastMod(uint value, uint divisor, ulong multiplier) => (uint)(((multiplier * value >> 32) + 1) * divisor >> 32);

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

    #endregion
}

internal record struct Member(int Id, string Name, TypeInfo Type, bool IsNullable, bool IsReadable = true, bool IsWritable = true, ImmutableArray<AttributeData> Attributes = default, bool IsMandatory = false, bool Exists = true)
{
    internal string? DefaultBang, Bang;
    internal bool IgnoresRight; 
    internal short MaxDepth = (short)(Type.IsIterable ? 1 : 0);
    internal readonly bool MaxDepthReached(string s)
    {
        var t = MaxDepth;
        if (t == 0)
            return true;

        string n = $"+{Id}+", ss;

        for (int nL = n.Length, start = Math.Abs(s.Length - n.Length), end = s.Length; start > -1 && t > -1 && end - start >= nL;)
        {
            if ((ss = s[start..end]) == n)
            {
                if (t-- == 0) return true;
                end = start + 1;
                start = end - nL;
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
    public readonly override string ToString() => $"({Type}){Name}";
}

internal struct TypeMapping(uint id, TypeInfo left, TypeInfo right, int _next)
{
    internal CodeBuilder
        TypeBuilder = default!,
        ReverseTypeBuilder = default!;
    internal CodeBuilder?
        Bootstrap = default,
        ReverseBootstrap = default;

    internal byte
        LeftMaxDepth = 2,
        RightMaxDepth = 2;

    internal readonly uint Id = id;
    internal int next = _next;
    internal readonly bool
        AreSameType = left.Id == right.Id,
        IsScalar = left.IsPrimitive && right.IsPrimitive,
        IsCollection = left.IsIterable && right.IsIterable,
        AreCompatible = left.IsCompatibleWith(right);
    internal readonly bool CanDepth = !left.IsPrimitive && !right.IsPrimitive
        && (left._typeSymbol.TypeKind, right._typeSymbol.TypeKind) is not (TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown, TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown);

    private readonly TypeInfo _left = left;
    private readonly TypeInfo _right = right;

    internal readonly ImmutableArray<ISymbol> LeftMembers => _left._typeSymbol.GetMembers();
    internal readonly ImmutableArray<ISymbol> RightMembers => _right._typeSymbol.GetMembers();

    internal bool? CanMap { get; set; }

    internal bool HasLeftToRightMap, HasRightToLeftMap;

    internal readonly void GatherLeft(ref TypeInfo? leftExistent, int leftId)
    {
        if (_left.Id == leftId)
            leftExistent = _left;
    }

    internal readonly void GatherRight(ref TypeInfo? rightExistent, int rightId)
    {
        if (_right.Id == rightId)
            rightExistent = _right;
    }

    public override readonly string ToString() => $"{_left.FullName} => {_right.FullName}";
}


internal sealed class TypeInfo
{
    internal static TypeInfo objectTypeSymbol = default!;

    internal readonly int Id;
    internal readonly string
        Name,
        FullName,
        FullNonGenericName;

    internal readonly bool
        //IsNullable,
        AllowsNull;

    internal readonly ITypeSymbol _typeSymbol;
    internal readonly TypeInfo? ItemType;
    internal readonly bool IsIterable, IsPrimitive, IsItemTypeNullable;
    internal readonly IterableType IterType;
    internal readonly string? AddMethod;
    internal readonly string? CountProperty = null!;

    internal TypeInfo(ITypeSymbol type)
    {
        Id = TestClass._comparer.GetHashCode(type);

        if (type.ToGlobalizedNonGenericNamespace() == "global::System.Nullable")
        {
            type = ((INamedTypeSymbol)type).TypeArguments[0];

            Name = type.ToTypeNameFormat() + "?";
            FullName = type.ToGlobalizedNamespace() + "?";
        }
        else
        {
            Name = type.ToTypeNameFormat();
            FullName = type.ToGlobalizedNamespace();
        }

        FullNonGenericName = type.ToGlobalizedNonGenericNamespace();

        IsPrimitive = type.IsPrimitive();
        AllowsNull = type.AllowsNull();
        _typeSymbol = type;

        IsIterable = IsEnumerableType(_typeSymbol, ref ItemType, ref IsItemTypeNullable, ref IterType, ref AddMethod, ref CountProperty);
    }

    bool IsEnumerableType(ITypeSymbol type, ref TypeInfo? itemType, ref bool isItemNullable, ref IterableType iterType, ref string? addMethod, ref string? countProperty)
    {
        if (type.SpecialType == SpecialType.System_String || type.IsPrimitive())
            return false;

        switch (FullNonGenericName)
        {
            case "global::System.Collections.Generic.Stack"
            :
                addMethod = "Push";
                countProperty = "Count";
                (itemType, isItemNullable) = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() is { } argType
                    ? (new TypeInfo(argType), argType.IsNullable())
                    : (objectTypeSymbol, false);

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                addMethod = "Enqueue";
                countProperty = "Count";
                (itemType, isItemNullable) = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() is { } argType2
                    ? (new TypeInfo(argType2), argType2.IsNullable())
                    : (objectTypeSymbol, false);

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                addMethod = "Add";
                countProperty = "Count";
                (itemType, isItemNullable) = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() is { } argType3
                    ? (new TypeInfo(argType3), argType3.IsNullable())
                    : (objectTypeSymbol, false);

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                countProperty = "Count";
                (itemType, isItemNullable) = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() is { } argType4
                    ? (new TypeInfo(argType4), argType4.IsNullable())
                    : (objectTypeSymbol, false);

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                iterType = IterableType.Enumerable;
                (itemType, isItemNullable) = ((INamedTypeSymbol)type).TypeArguments.FirstOrDefault() is { } argType5
                    ? (new TypeInfo(argType5), argType5.IsNullable())
                    : (objectTypeSymbol, false);

                return true;

            default:
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                {
                    countProperty = "Length";
                    itemType = new(elType);
                    isItemNullable = elType.IsNullable();
                    iterType = IterableType.Array;

                    return true;
                }
                else
                    foreach (var item in type.AllInterfaces)
                        if (IsEnumerableType(item, ref itemType, ref isItemNullable, ref iterType, ref addMethod, ref countProperty))
                            return true;
                break;
        }

        return false;
    }

    internal bool Equals(TypeInfo obj) => TestClass._comparer.Equals(_typeSymbol, obj._typeSymbol);

    public override string ToString() => FullName;

    internal static int GetHashCode(TypeInfo obj) => TestClass._comparer.GetHashCode(obj._typeSymbol);
    internal static int GetHashCode(ISymbol obj) => TestClass._comparer.GetHashCode(obj);
    public override int GetHashCode() => TestClass._comparer.GetHashCode(_typeSymbol);

    internal ImmutableArray<ISymbol> GetMembers() => _typeSymbol.GetMembers();
    internal ImmutableArray<AttributeData> GetAttributes() => _typeSymbol.GetAttributes();

    internal bool IsCompatibleWith(TypeInfo right) => IsPrimitive && right.IsPrimitive || IsIterable && right.IsIterable;
}

internal enum IterableType { Collection, Enumerable, Array }

//internal struct TypeMapping : IMapping<CodeType>
//{
//    internal CodeType Left => throw new NotImplementedException();

//    internal CodeType Right => throw new NotImplementedException();

//    internal CodeBuilder BuildProperty => throw new NotImplementedException();

//    internal CodeBuilder BuildMethod => throw new NotImplementedException();

//    internal CodeBuilder BuildEnumerable => throw new NotImplementedException();
//}

//internal struct CodeType
//{

//}

//internal struct TypeMember
//{

//}

//internal interface IMapping<T> : IMapping
//{
//    T Left { get; }
//    T Right { get; }
//}


delegate void CodeBuilder(string path, string left, string right, Indent indent, ref string? comma);

internal sealed class Indent
{
    private string spaces = "    ";
    public static Indent operator ++(Indent from)
    {
        from.spaces += "    ";
        return from;
    }
    public static Indent operator +(Indent from, int i)
    {
        from.spaces += new string(' ', i * 4);
        return from;
    }
    public static Indent operator --(Indent from)
    {
        if (from.spaces.Length > 4)
            from.spaces = from.spaces[..^4];
        return from;
    }
    public static Indent operator -(Indent from, int i)
    {
        i *= 4;
        if (from.spaces.Length > i)
            from.spaces = from.spaces[..^i];
        return from;
    }

    public override string ToString() => spaces;
}