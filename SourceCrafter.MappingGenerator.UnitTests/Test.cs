//Testing utils
using Xunit;
using System.Text;

// Analyzer 
using SourceCrafter.Bindings;

//Testing purpose
using FluentAssertions;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceCrafter.Bindings.Attributes;
using SourceCrafter.Bindings.Helpers;
using SourceCrafter.Bindings.Constants;
using SourceCrafter.MappingGenerator;
using SourceCrafter.Mappings;

namespace SourceCrafter.UnitTests;

public class TestImplicitMapper
{

    [Fact]
    public static void GetSomeType()
    {
        GetRootAndModel(@"",
            out var compilation,
            out _,
            out _
        );

        compilation.GetTypeByMetadataName("System.Span`1");
    }

    [Fact]
    public static void ParseAssembly()
    {
        GetRootAndModel(@"using SourceCrafter.Binding.Attributes;
using SourceCrafter.UnitTests;

[assembly:
    Bind<WindowsUser, UserDto>,
    Bind<WindowsUser, User>,
    Bind<User, UserDto>]",
            out var compilation,
            out var root,
            out var model,
            typeof(User), 
            typeof(BindAttribute<,>)
        );


        StringBuilder code = new(@"namespace SourceCrafter.Mappings;

public static partial class Mappers
{");

        var assemblyAtributes = compilation.Assembly
            .GetAttributes()
            .Where(c => c.AttributeClass?.ToGlobalizedNonGenericNamespace() == "global::SourceCrafter.Binding.Attributes.BindAttribute")
            .Select(c => new MapInfo(c.AttributeClass!.TypeArguments[0], c.AttributeClass!.TypeArguments[1], MappingKind.All, ApplyOn.None))
            .ToImmutableArray();

        new Generator()
            .BuildCode(
                code,
                compilation,
                ImmutableArray<MapInfo>.Empty,
                assemblyAtributes);

        code.Append("\n}");

        //new User().ToUserDto().Asignees!.ElementAt(0).

        //Trace.WriteLine(code.ToString());
    }


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
    [Fact]
    public void TestClass()
    {
        DateTime today = DateTime.Today;
        (int, string)[] roles = [(0, "admin"), (1, "publisher")];

        var userDto = new UserDto
        {
            FullName = "Gil Mora, Pedro",
            Age = 32,
            DateOfBirth = today,
            Count = 5,
            TotalAmount = 45.6m,
            MainRole = roles[0]
        };

        var fromDto = userDto.ToUser();
        fromDto.Age.Should().Be(32);
        fromDto.FirstName.Should().Be("Pedro");
        fromDto.LastName.Should().Be("Gil Mora");
        fromDto.DateOfBirth.Should().Be(today);
        fromDto.Balance.Should().Be(45.6);
        fromDto.Count.Should().Be(5);

        var fromModel = fromDto.ToUserDto();
        fromModel.Age.Should().Be(32);
        fromModel.FullName.Should().Be("Gil Mora, Pedro");
        fromModel.DateOfBirth.Should().Be(today);
        fromModel.TotalAmount.Should().Be(45.6m);
        fromModel.Count.Should().Be(5);
    }

    private static void GetRootAndModel(string code, out CSharpCompilation compilation, out SyntaxNode root, out SemanticModel model, params Type[] assemblies)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);

        root = tree.GetRoot();

        compilation = CSharpCompilation
            .Create(
                "Temp",
                new[] { tree },
                assemblies
                    .Select(a => a.Assembly.Location)
                    .Append(typeof(object).Assembly.Location)
                    .Distinct()
                    .Select(r => MetadataReference.CreateFromFile(r))
                    .ToImmutableArray());

        model = compilation.GetSemanticModel(tree);
    }
}