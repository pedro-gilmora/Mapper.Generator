# ✨ **Mapper.Generator**: Generates simple partial classes with explicit conversion operators based on predefined metadata 

### Given the following classes
```csharp
public partial class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static explicit operator Role((int id, string name) role) => new() { Id = role.id, Name = role.name };
    public static explicit operator (int id, string name)(Role role) => (role.Id, role.Name);
}
public static class Mapper
{
    public static User Map(UserDto from)
    {
        return new User
        {
            Age = from.Age,
            DateOfBirth = from.DateOfBirth,
            Balance = (double)from.TotalAmount,
            Roles = from.Roles.Select(el => (Role)el).ToList(),
            MainRole = (Role)from.MainRole
        }
            .MapName(from);
    }
}

[Map<UserDto>(nameof(Mapper.Map))]
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

    internal User MapName(UserDto user)
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
```

should generates the following view model class (based on the previous definition example):

```csharp
public partial class User
{
    public static explicit operator User(UserDto from)
    {
        return Mapper.Map(from);
    }
}

public partial class UserDto
{
    public static explicit operator UserDto(User from)
    {
        return new UserDto {
	        FullName = MapFullName(from),
	        Age = from.Age,
	        DateOfBirth = from.DateOfBirth,
	        Roles = from.Roles.Select(el => ((int, string))el).ToArray(),
	        MainRole = ((int, string))from.MainRole,
	        TotalAmount = (decimal)from.Balance
        };
    }
}
```