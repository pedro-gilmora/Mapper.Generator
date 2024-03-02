using SourceCrafter.Binding.Attributes;
using System.Linq;
using System.Text;
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SourceCrafter.Mvvm.Helpers
{
    public class Test
    {

        readonly SymbolsMap map = new();

        readonly StringBuilder code = new(@"namespace SourceCrafter.Mappings;
	
	public static partial class Mappers
	{");

        [Fact]
        public void Init()
        {
            GetRootAndModel(@"using SourceCrafter.Binding.Attributes;
[assembly: Bind<User, UserDto>]
", out var compilation, out var root, out var model, typeof(BindAttribute<,>));

            foreach (var attr in compilation.Assembly.GetAttributes())
            {
                if (attr.AttributeClass?.ToGlobalizedNonGenericNamespace() == "global::SourceCrafter.Binding.Attributes.BindAttribute")
                {
                    var attrArg0 = attr.AttributeClass.TypeArguments[0];
                    var attrArg1 = attr.AttributeClass.TypeArguments[1];

                    CreateTypeMapping(
                        compilation,
                        new()
                        {
                            Type = attrArg0,
                        },
                        new()
                        {
                            Type = attrArg1
                        });

                }
            }

        }

        Binding CreateTypeMapping(Compilation compilation, SymbolInfo toInfo, SymbolInfo fromInfo, int level = 0)
        {
            ref var bindings = ref map.TryGetOrAddBindings(toInfo.Type, fromInfo.Type, out var bindingExists);

            if (bindingExists) return bindings;

            var toMembersE = toInfo.Type.GetMembers().GetEnumerator();

            while (toMembersE.MoveNext())
            {
                if (IsNotMappableMember(toMembersE.Current, out var toMemberInfo)) continue;

                var toMember = toMembersE.Current;

                var fromMembersE = fromInfo.Type.GetMembers().GetEnumerator();

                while (fromMembersE.MoveNext())
                {
                    //Should discard property if not matching requirements
                    if (IsNotMappableMember(fromMembersE.Current, out var fromMemberInfo)) continue;

                    var fromMember = toMembersE.Current;

                    var (iteratorBuilder, propsBuilder) = (bindings.propsBuilders, bindings.propsBuilders);

                    bool nameEquals = toMember.Name != fromMember.Name,
                        reverse = false,
                        hasMapping = false;

                    //If not equal names and not mappable by atribute
                    if (!nameEquals && !AreMappableByAttribute(toMember, fromMember))
                    {
                        //Before discard member mappings check if there's a reverse mapping
                        if (!AreMappableByAttribute(fromMember, toMember))
                            continue;

                        (fromMemberInfo, toMemberInfo, iteratorBuilder, propsBuilder) = (toMemberInfo, fromMemberInfo, bindings.reverseIteratorBuilders, bindings.reversePropsBuilders);

                        reverse = true;
                    }
                reverse:
                    var required = !(toMemberInfo.AllowsNull || toMemberInfo.AllowsNull) && (fromMemberInfo.AllowsNull || fromMemberInfo.IsNullable) ? "!" : "";

                    //Based on conversion
                    if (compilation.ClassifyConversion(toMemberInfo.Type, fromMemberInfo.Type) is { Exists: var exists } conv && conv.IsExplicit)
                    {
                        //If is by ref
                        bindings.propsBuilders += conv.IsReference
                            ? (string to, string from, Indent indent, ref string? comma)
                                => code.AppendFormat(@"{0}
{1}    {2} = {3}.{4} as {5}", comma ??= ",", toInfo.MemberName, from, fromMemberInfo.MemberName, fromMemberInfo.TypeName.TrimEnd('?'))
                            //Is by value
                            : (string to, string from, Indent indent, ref string? comma) =>
                                code.AppendFormat(@"{0}
{1}    {2} = ({3}){4}.{5}{6}", comma ??= ",", toInfo.MemberName, fromMemberInfo.TypeName, from, fromMemberInfo.MemberName, required);
                    }
                    //If there's a implicit conversion
                    else if (exists)
                    {
                        bindings.propsBuilders +=

                            fromMemberInfo.IsNullable

                                ? (string to, string from, Indent indent, ref string? comma) =>
                            code.AppendFormat(@"{0}
{1}    {2} = {3}.{4} is {{}} {3}{4} ? {3}{4} : default{5}", comma ??= ",", indent, toMemberInfo.MemberName, from, fromMemberInfo.MemberName, required)

                                : (string to, string from, Indent indent, ref string? comma) =>
                            code.AppendFormat(@"{0}
{1}    {2} = {3}.{4}{5}", comma ??= ",", indent, toMemberInfo.MemberName, from, fromMemberInfo.MemberName, required);

                    }
                    /*
                    //			else if (toMemberInfo.NotPrimitive && fromMemberInfo.NotPrimitive)
                    //			{
                    //				if (toMemberInfo.GetHashCode(fromMemberInfo) == bindings.id)
                    //				{
                    //					if (TryCreateNestedType(toMemberInfo, fromMemberInfo, bindings, out CodeBuilder builder))
                    //					{
                    //						propsBuilder += bindings.typeBuilder;
                    //					}
                    //
                    //					if (TryCreateNestedType(fromMemberInfo, toMemberInfo, bindings, out builder))
                    //					{
                    //						propsBuilder += builder;
                    //					}
                    //					
                    //					break;
                    //				}
                    //				else
                    //				{
                    //					
                    //				}
                    //				
                    //				continue;
                    //			}
                    */

                    if (reverse)
                    {
                        if (hasMapping)
                        {
                            bindings.reverseIteratorBuilders += iteratorBuilder;
                            bindings.reversePropsBuilders += propsBuilder;
                        }
                    }
                    else
                    {
                        if (hasMapping)
                        {
                            bindings.iteratorBuilders += iteratorBuilder;
                            bindings.propsBuilders += propsBuilder;
                        }
                        if (AreMappableByAttribute(fromMember, toMember))
                        {
                            (fromMemberInfo, toMemberInfo, iteratorBuilder, propsBuilder) = (toMemberInfo, fromMemberInfo, bindings.reverseIteratorBuilders, bindings.reversePropsBuilders);
                            reverse = true;
                            goto reverse;
                        }
                    }
                    break;
                }
            }
            return bindings;
        }

        static void CreateEnumerableMapping(SymbolInfo toMemberInfo, SymbolInfo fromMemberInfo)
        {

        }

        static bool IsNotMappableMember(ISymbol toMember, out SymbolInfo toMemberInfo)
            => !(toMemberInfo = toMember switch
            {
                IPropertySymbol { IsIndexer: false, Type: { } type, DeclaredAccessibility: var accessibility, IsReadOnly: var isReadonly }
                    => new()
                    {
                        Accessibility = accessibility,
                        IsReadOnly = isReadonly,
                        IsMappable = true,
                        Type = type,
                        MemberName = toMember.ToNameOnly()
                    },
                IFieldSymbol { Type: { } type, DeclaredAccessibility: var accessibility, IsReadOnly: var isReadonly }
                    => new()
                    {
                        Accessibility = accessibility,
                        IsReadOnly = isReadonly,
                        IsMappable = true,
                        Type = type,
                        MemberName = toMember.ToNameOnly()
                    },
                _ => default
            }).IsMappable;

        static bool AreMappableByAttribute(ISymbol toMember, ISymbol fromMember)
        {
            return toMember is IPropertySymbol { IsReadOnly: false } or IFieldSymbol { IsReadOnly: false }
                    && toMember.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
                    && fromMember is
                        IPropertySymbol { IsWriteOnly: false, DeclaredAccessibility: Accessibility.Public or Accessibility.Internal }
                        or IFieldSymbol { DeclaredAccessibility: Accessibility.Public or Accessibility.Internal };

        }

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
                        .Select(l => MetadataReference.CreateFromFile(l))
                        .ToArray()
                );
            SymbolInfo.objectTypeSymbol = compilation.GetTypeByMetadataName("System.Object")!;
            model = compilation.GetSemanticModel(tree);
        }

        public class User
        {
            public string FullName { get; set; }
            public int Age { get; set; }
        }

        public class UserDto
        {
            public string FullName { get; set; }
            public int Age { get; set; }
        }

    }
}