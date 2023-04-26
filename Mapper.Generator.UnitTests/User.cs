using FluentAssertions;
using Mapper.Generator.Attributes;
using Mapper.Generator.Constants;
using Xunit;

namespace Mapper.Generator.UnitTests;

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
public static class Mapper
{
    public static User Map(UserDto from)
    {
        return new User
        {
            Count = from.Count,
            Age = from.Age,
            Unwanted = from.Unwanted,
            DateOfBirth = from.DateOfBirth,
            Roles = from.Roles.Select(el => (Role)el).ToList(),
            MainRole = (Role)from.MainRole,
            FullName = from.FullName
        };
    }
}
[Map<UserDto>(nameof(Mapper.Map))]
public partial class User
{
    public string FirstName { get; set; } = null!;
    public string? LastName { get; set; }
    public int Age { get; set; }
    [Ignore(Ignore.OnSource)]
    public string? Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
    [Map(nameof(UserDto.TotalAmount), Ignore.OnTarget)]
    public double Balance { get; set; }
    public List<Role> Roles { get; set; } = new();
    public Role MainRole { get; set; }

    internal string FullName
    {
        get => $"{ LastName?.Trim()}, { FirstName?.Trim()}";
        set => (FirstName, LastName) = value?.Split(", ") switch
        {
            [{ } lastName, { } firstName] => (firstName.Trim(), lastName.Trim()),
            [{ } firstName] => (firstName, null!),
            _ => (null!, null!)
        };
    }
}

public partial class UserDto
{
    public string FullName
    {
        get;
        set;
    } = null!;
    public int Count { get; set; }
    public int Age { get; set; }
    public string Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
    public (int, string)[] Roles { get; set; } = Array.Empty<(int, string)>();
    public (int, string) MainRole { get; set; } = default;
    public decimal TotalAmount { get; set; }
}

public class TestImplicitMapper {

    [Fact]
    void TestClass() {
        DateTime today = DateTime.Today;
        (int, string)[] roles = { (0, "admin"), (1, "publisher") };

        var user = new UserDto
        {
            FullName = "Gil Mora, Pedro",
            Age = 32,
            DateOfBirth = today,
            Roles = roles,
            Count = 5,
            TotalAmount = 45.6m,
            MainRole = roles[0]
        };
        User fromDto = (User)user;
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

}
