using FluentAssertions;
using Mapper.Generator.Attributes;
using Xunit;

namespace Mapper.Generator.UnitTests;

public partial class Role
{

    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static explicit operator Role((int id, string name) role) => new() { Id = role.id, Name = role.name };
    public static explicit operator (int id, string name)(Role role) => (role.Id, role.Name);
}

[Map<UserDto>]
public partial class User
{
    public string FirstName { get; set; } = null!;
    public string? LastName { get; set; }
    public int Age { get; set; }

    public DateTime DateOfBirth { get; set; }
    [MapFrom(nameof(UserDto.TotalAmount))]
    public double Balance { get; set; }
    public List<Role> Roles { get; set; } = new();
    public Role MainRole { get; set; }

    User MapName(UserDto user)
    {
        (FirstName, LastName) = user.FullName?.Split(",") switch
        {
            [{ } lastName, { } firstName] => (firstName.Trim(), lastName.Trim()),
            [{ } firstName] => (firstName, null),
            _ => (null!, null)
        };
        return this;
    }
}

[Map<User>]
public partial class UserDto
{
    [MapWith(nameof(MapFullName))]
    public string FullName { get; set; } = null!;
    public int Age { get; set; }    
    public DateTime DateOfBirth { get; set; }
    public (int, string)[] Roles { get; set; } = Array.Empty<(int, string)>();
    public (int, string) MainRole { get; set; } = default;
    [MapFrom(nameof(User.Balance))]
    public decimal TotalAmount { get; set; }
    static string MapFullName(User user) => $"{user.LastName?.Trim()}, {user.FirstName?.Trim()}";
}

public class TestImplicitMapper {

    [Fact]
    void TestClass() {
        DateTime today = DateTime.Today;
        (int, string)[] roles = { (0, "admin"), (1, "publisher") };
       
        User fromDto = (User)new UserDto { FullName = "Gil Mora, Pedro", Age = 32, DateOfBirth = today, Roles = roles, MainRole = roles[0] };
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
