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
            Balance = (double)from.TotalAmount,
            Count = from.Count,
            Roles = from.Roles.Select(el => (Role)el).ToList(),
            DateOfBirth = from.DateOfBirth,
            MainRole = (Role)from.MainRole,
            Age = from.Age
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
```

...should generates the following mapping class (based on the previous definition example):

```csharp
public partial class User
{
    public static explicit operator User(UserDto from)
    {
        return Mapper.Map(from);
    }
			
    public static explicit operator UserDto(User from)
    {
        return new UserDto {
	        Count = from.Count,
	        Age = from.Age,
	        DateOfBirth = from.DateOfBirth,
	        TotalAmount = (decimal)from.Balance,
	        Roles = from.Roles.Select(el => ((int, string))el).ToArray(),
	        MainRole = ((int, string))from.MainRole,
	        FullName = from.FullName
        };
    }
}
```