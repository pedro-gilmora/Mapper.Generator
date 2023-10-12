//Testing utils
using Xunit;
using System.Text;

// Analyzer 
using SourceCrafter.Mapping.Attributes;

//Testing purpose
using System.Security.Principal;
using FluentAssertions;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceCrafter.UnitTests;

public class TestImplicitMapper
{

    //    [Fact]
    //    public static void ParseAssembly()
    //    {
    //        GetRootAndModel(@"
    //[assembly: SourceCrafter.Mapping.Attributes.Map<
    //    SourceCrafter.UnitTests.UserDto,
    //    SourceCrafter.UnitTests.WindowsUser>]
    //[assembly:
    //    SourceCrafter.Mapping.Attributes.Map<
    //        SourceCrafter.UnitTests.User,
    //        SourceCrafter.UnitTests.UserDto>]
    //[assembly:
    //    SourceCrafter.Mapping.Attributes.Map<
    //        SourceCrafter.UnitTests.User,
    //        SourceCrafter.UnitTests.UserMiniDto>]",
    //            new[]{
    //                typeof(Attribute),
    //                typeof(User),
    //                typeof(IgnoreAttribute),
    //                typeof(MapAttribute),
    //                typeof(MapAttribute<>),
    //                typeof(MapAttribute<,>)
    //            },
    //            out var compilation,
    //            out var root,
    //            out var model
    //        );

    ////        MappingGenerator.MappersFromAssemblyAttributes(
    ////            compilation, 
    ////            compilation.Assembly, 
    ////            out var mappersCount, 
    ////            out var code
    ////#if DEBUG
    ////            , out var extra
    ////#endif
    ////        );

    ////#if DEBUG
    ////        code.Append($@"
    ////}}
    /////*
    ////Extras:
    ////-------{extra}
    ////*/
    ////");
    ////#endif

    //    }


    //    [Fact]
    //    public static void ParseCode()
    //    {
    //        GetRootAndModel(
    //            "SourceCrafter.UnitTests.User user;",
    //            new[] {
    //                typeof(MapAttribute),
    //                typeof(Attribute),
    //                typeof(User)
    //            },
    //            out var compilation,
    //            out var root,
    //            out var semanticModel
    //            );

    //        foreach (var cls in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
    //        {
    //            if (semanticModel.GetSymbolInfo(cls.Type).Symbol is not ITypeSymbol type) continue;
    //            StringBuilder code = new();
    //#if DEBUG
    //            string extra = "";
    //#endif
    ////            MappingGenerator.GetConverters(code, type.GetAttributes(), type, compilation, new HashSet<string>()
    ////#if DEBUG
    ////                , ref extra
    ////#endif
    ////                , false);

    //#if DEBUG
    //            code.Append($@"
    //}}
    ///*
    //Extras:
    //-------{extra}
    //*/
    //");

    //#else
    //            code.Append(@"
    //}");
    //#endif
    //        }
    //    }
    //#if DEBUG
    //    [Fact]
    //    public void TestExternalClass()
    //    {
    //#pragma warning disable CA1416 // Validar la compatibilidad de la plataforma
    //        var winUser = WindowsIdentity.GetCurrent()!.ToWindowsUser();
    //#pragma warning restore CA1416 // Validar la compatibilidad de la plataforma
    //        winUser.Should().NotBeNull();
    //        winUser.Name.Should().Be("DEVSTATION\\Pedro");
    //        winUser.IsAuthenticated.Should().BeTrue();
    //        //var winId = new WindowsUser { Name = "DEVSTATION\\Pedro" }.ToWindowsIdentity();
    //        //winId.Should().NotBeNull();
    //        //winId.Name?.Should().Be("DEVSTATION\\Pedro");
    //        //winId.IsAuthenticated.Should().BeTrue();
    //    }
    //#endif
    //[Fact]
    //public void TestClass()
    //{
    //    DateTime today = DateTime.Today;
    //    (int, string)[] roles = { (0, "admin"), (1, "publisher") };

    //    var userDto = new UserDto
    //    {
    //        FullName = "Gil Mora, Pedro",
    //        Age = 32,
    //        DateOfBirth = today,
    //        Roles = roles,
    //        Count = 5,
    //        TotalAmount = 45.6m,
    //        MainRole = roles[0]
    //    };
    //    User fromDto = (User)userDto;
    //    fromDto.Age.Should().Be(32);
    //    fromDto.FirstName.Should().Be("Pedro");
    //    fromDto.LastName.Should().Be("Gil Mora");
    //    fromDto.DateOfBirth.Should().Be(today);
    //    fromDto.Roles.Select(r => ((int, string))r).Should().BeEquivalentTo(roles);

    //    UserDto fromModel = (UserDto)fromDto;
    //    fromModel.Age.Should().Be(32);
    //    fromModel.FullName.Should().Be("Gil Mora, Pedro");
    //    fromModel.DateOfBirth.Should().Be(today);
    //    fromModel.Roles.Should().BeEquivalentTo(roles);
    //}

    private static void GetRootAndModel(string code, Type[] assemblies, out CSharpCompilation compilation, out SyntaxNode root, out SemanticModel model)
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
                    .Select(r => MetadataReference.CreateFromFile(r))
                    .ToImmutableArray());

        model = compilation.GetSemanticModel(tree);
    }
}