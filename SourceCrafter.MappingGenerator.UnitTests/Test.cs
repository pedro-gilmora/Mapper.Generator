//Testing utils
using Xunit;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Analyzer 
using SourceCrafter.Mapping.Attributes;
using SourceCrafter.Mapping.Constants;

//Testing purpose
using System.Security.Principal;
using FluentAssertions;
using System.Collections.Immutable;

namespace SourceCrafter.UnitTests;

public partial class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static explicit operator Role((int id, string name) role) => new() { Id = role.id, Name = role.name };
    public static explicit operator Role?((int id, string name)? role) => role is { } a ? new() { Id = a.id, Name = a.name } : null;
    public static explicit operator (int id, string name)(Role role) => (role.Id, role.Name);
    public static explicit operator (int id, string name)?(Role? role) => role != null ? (role.Id, role.Name) : null;
}

public partial class User
{
    public int Count { get; set; }

}
[Map<UserDto>]
public interface IUser
{
    string FullName { get; set; }
    int Age { get; set; }

    [Ignore(Ignore.OnSource)]
    string? Unwanted { get; set; }
    DateTime DateOfBirth { get; set; }
    [Map(nameof(UserDto.TotalAmount), Ignore.OnTarget)]
    double Balance { get; set; }
    List<Role> Roles { get; set; }
    Role? MainRole { get; set; }
}

public partial class User : IUser
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
    string IUser.FullName { get => FullName; set => FullName = value; }
    [Ignore(Ignore.OnSource)]
    public string? Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
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
    public (int, string)? MainRole { get; set; }
    public decimal TotalAmount { get; set; }
}
//[Map<WindowsIdentity>(nameof(GlobalMappers.MapToWindowsIdentity))]
public partial class WindowsUser
{
    public string Name { get; set; } = null!;
    public bool IsAuthenticated { get; set; }
}

public class TestImplicitMapper
{

    [Fact]
    public static void ParseAssembly()
    {
        GetRootAndModel(@"
[assembly: SourceCrafter.Mapping.Attributes.Map<
    SourceCrafter.UnitTests.IUser,
    SourceCrafter.UnitTests.UserDto>]",
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

            MappingGenerator.BuildConverters(code, fromClass, toClass, toMapper, fromMapper, model, false, builders, attr, ignore
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


    [Fact]
    public static void ParseCode()
    {
        GetRootAndModel(
            "SourceCrafter.UnitTests.IUser user;",
            new[] { 
                typeof(MapAttribute),
                typeof(Attribute),
                typeof(User)
            },
            out var compilation, 
            out var root, 
            out var semanticModel
            );

        foreach (var cls in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
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
#pragma warning disable CA1416 // Validar la compatibilidad de la plataforma
        var winUser = WindowsIdentity.GetCurrent()!.ToWindowsUser();
#pragma warning restore CA1416 // Validar la compatibilidad de la plataforma
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
                    .Select(r => MetadataReference.CreateFromFile(r))
                    .ToImmutableArray());

        model = compilation.GetSemanticModel(tree);
    }
}