//Testing utils
using Xunit;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Analyzer 
using RogueGen.Mapping.Attributes;
using RogueGen.Mapping.Constants;

//Testing purpose
using System.Security.Principal;
using FluentAssertions;

namespace RogueGen.UnitTests;

public partial class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static explicit operator Role((int id, string name) role) => new() { Id = role.id, Name = role.name };
    public static explicit operator (int id, string name)(Role role) => (role.Id, role.Name);
}

public partial class User
{
    public int Count { get; set; }

}

public partial class UserBase
{

    internal string FullName
    {
        get => $"{LastName?.Trim()}, {FirstName?.Trim()}";
        set => (FirstName, LastName) = value?.Split(", ") switch
        {
            [{ } lastName, { } firstName] => (firstName.Trim(), lastName.Trim()),
            [{ } firstName] => (firstName, null!),
            _ => (null!, null!)
        };
    }
    public string FirstName { get; set; } = null!;
    public string? LastName { get; set; }
    public int Age { get; set; }
}


[Map<UserDto>]
public partial class User : UserBase
{
    [Ignore(Ignore.OnSource)]
    public string? Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
    [Map(nameof(UserDto.TotalAmount), Ignore.OnTarget)]
    public double Balance { get; set; }
    public List<Role> Roles { get; set; } = new();
    public Role? MainRole { get; set; }
}

public partial class UserDto
{
    public string FullName { get; set; } = null!;
    public int Count { get; set; }
    public int Age { get; set; }
    public string? Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
    public (int, string)[] Roles { get; set; } = Array.Empty<(int, string)>();
    public (int, string) MainRole { get; set; } = default;
    public decimal TotalAmount { get; set; }
}
[Map<WindowsIdentity>(nameof(GlobalMappers.MapToWindowsIdentity))]
public partial class WindowsUser
{
    public string Name { get; set; } = null!;
    public bool IsAuthenticated { get; set; }
}

public class TestImplicitMapper
{

#if DEBUG
    [Fact]
    public static void ParseAssembly()
    {
        GetRootAndModel(@"[assembly: RogueGen.Mapping.Attributes.Map<
    RogueGen.UnitTests.WindowsUser,
    System.Security.Principal.WindowsIdentity>(
        nameof(RogueGen.GlobalMappers.MapToWindowsIdentity))]",
            new[]{
                typeof(Attribute),
                typeof(User),
                typeof(IgnoreAttribute),
                typeof(MapAttribute),
                typeof(MapAttribute<>),
                typeof(MapAttribute<,>)
            },
            out var compilation,
            out var root,
            out var model
        );
        
        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (attr.AttributeClass is null or not { MetadataName: "MapAttribute`2" }) 
                continue;

            StringBuilder code = new();
#if DEBUG
            string extra = "";
#endif
            HashSet<string> usings = new();
            Ignore ignore = default;
            List<string> builders = new();
            var fromClass = attr.AttributeClass.TypeArguments[0];
            var toClass = attr.AttributeClass.TypeArguments[1];

            MappingGenerator.TryResolveMappers(compilation, attr, fromClass, toClass, out var fromMapper, out var toMapper);

            MappingGenerator.BuildConverters(code, fromClass, toClass, toMapper, fromMapper, model, usings, false, builders, attr, ignore
#if DEBUG
        , ref extra
#endif
        );

#if DEBUG
            code.Append($@"
}}
/*
Extras:
-------{extra}
*/
");

#else
            code.Append(@"
}");
#endif
        }
    }
#endif


    [Fact]
    public static void ParseCode()
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(@"RogueGen.UnitTests.User user;");

        // get the root node of the syntax tree
        var root = tree.GetRoot();

        var mapAttrReference = MetadataReference.CreateFromFile(typeof(MapAttribute).Assembly.Location);

        var compilation = CSharpCompilation
            .Create(
                "Temp",
                new[] { tree },
                new[]{
                    MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
                    mapAttrReference,
                    MetadataReference.CreateFromFile(typeof(User).Assembly.Location)
                });
        var e = compilation.SyntaxTrees;
        var semanticModel = compilation.GetSemanticModel(tree);
        foreach (var cls in tree.GetRoot().DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            if (semanticModel.GetSymbolInfo(cls.Type).Symbol is not ITypeSymbol type) continue;
            StringBuilder code = new();
#if DEBUG
            string extra = "";
#endif
            MappingGenerator.GetConverters(code, type.GetAttributes(), type, semanticModel, new HashSet<string>()
#if DEBUG
                , ref extra
#endif
                , false);

#if DEBUG
            code.Append($@"
}}
/*
Extras:
-------{extra}
*/
");

#else
            code.Append(@"
}");
#endif
        }
    }
#if DEBUG
    [Fact]
    public void TestExternalClass()
    {
        var winUser = WindowsIdentity.GetCurrent()!.ToWindowsUser();
        winUser.Should().NotBeNull();
        winUser.Name.Should().Be("DEVSTATION\\Pedro");
        winUser.IsAuthenticated.Should().BeTrue();
        //var winId = new WindowsUser { Name = "DEVSTATION\\Pedro" }.ToWindowsIdentity();
        //winId.Should().NotBeNull();
        //winId.Name?.Should().Be("DEVSTATION\\Pedro");
        //winId.IsAuthenticated.Should().BeTrue();
    }
#endif
    [Fact]
    public void TestClass()
    {
        DateTime today = DateTime.Today;
        (int, string)[] roles = { (0, "admin"), (1, "publisher") };

        var userDto = new UserDto
        {
            FullName = "Gil Mora, Pedro",
            Age = 32,
            DateOfBirth = today,
            Roles = roles,
            Count = 5,
            TotalAmount = 45.6m,
            MainRole = roles[0]
        };
        User fromDto = (User)userDto;
        fromDto.Age.Should().Be(32);
        fromDto.FirstName.Should().Be("Pedro");
        fromDto.LastName.Should().Be("Gil Mora");
        fromDto.DateOfBirth.Should().Be(today);
        fromDto.Roles.Select(r => ((int, string))r).Should().BeEquivalentTo(roles);

        UserDto fromModel = (UserDto)fromDto;
        fromModel.Age.Should().Be(32);
        fromModel.FullName.Should().Be("Gil Mora, Pedro");
        fromModel.DateOfBirth.Should().Be(today);
        fromModel.Roles.Should().BeEquivalentTo(roles);
    }

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
                    .Select(l => MetadataReference.CreateFromFile(l))
                    .ToArray()
            );

        model = compilation.GetSemanticModel(tree);
    }

}