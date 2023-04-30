# ✨ **Mapper.Generator**: Source generator for conversion operators and static methods

![deploy status](https://github.com/pedro-gilmora/Mapper.Generator/actions/workflows/dotnet.yml/badge.svg)

## ⚙️ Supports
- Assembly global mappings
- Map inherited members
- Custom static mapper methods
- Property level mapping
- Ignore mapping behaviors (Levels: assembly, class, property)
- Easy to use variants
  - Extension method _(Generated through assembly attributes)_
    ```cssharp 
      var typeA = typeB.ToTypeA();
    ```
  - Explicit conversion operation 
    ```cssharp 
      var typeA = (TypeA)typeB;
    ```

---
## 🏛️ Scenarios

### Assembly level 
>##### Recommended for unmodifiable or external classes

#### Attribute specification (`[assembly: Map<TTarget, TSource>]`)
```csharp
[assembly: Map<WindowsUser,WindowsIdentity>(nameof(GlobalMappers.MapToWindowsIdentity))]
```
#### Generated content (`class <namespace>.GlobalMappers`)
```csharp
namespace Test;

public static partial class GlobalMappers {
    
    public static Test.WindowsUser ToWindowsUser(this System.Security.Principal.WindowsIdentity from)
    {
        return new Test.WindowsUser {
	        Name = from.Name,
	        IsAuthenticated = from.IsAuthenticated
        };
    }
    public static System.Security.Principal.WindowsIdentity ToWindowsIdentity(this Test.WindowsUser from)
    {
        return Test.GlobalMappers.MapToWindowsIdentity(from);
    }
}
```
---

### Having a project file for a `GlobalMappers` partial class
```csharp    
public static partial class GlobalMappers
{
    public static WindowsIdentity MapToWindowsIdentity(this WindowsUser from) 
    {
        return new(from.Name);
    }
}
```
---

## Class level

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

```

##### `[Map<UserDto>] public partial class User` produces:
```csharp
namespace Test;

public partial class User
{    
    public static explicit operator Test.User(Test.UserDto from)
    {
        return new Test.User {
	        FullName = from.FullName,
	        Age = from.Age,
	        Count = from.Count,
	        Unwanted = from.Unwanted,
	        DateOfBirth = from.DateOfBirth,
	        Roles = from.Roles.Select(el => (Role)el).ToList(),
	        MainRole = (Role)from.MainRole
        };
    }
    public static explicit operator Test.UserDto(Test.User from)
    {
        return new Test.UserDto {
	        FullName = from.FullName,
	        Age = from.Age,
	        Count = from.Count,
	        DateOfBirth = from.DateOfBirth,
	        TotalAmount = (decimal)from.Balance,
	        Roles = from.Roles.Select(el => ((int, string))el).ToArray(),
	        MainRole = ((int, string))from.MainRole
        };
    }
}
```

##### `Map<WindowsIdentity>(nameof(GlobalMappers.MapToWindowsIdentity))] public partial class WindowsUser` produces:
```csharp
namespace Test;

public partial class WindowsUser
{    
    public static explicit operator Test.WindowsUser(System.Security.Principal.WindowsIdentity from)
    {
        return new Test.WindowsUser {
	        Name = from.Name,
	        IsAuthenticated = from.IsAuthenticated
        };
    }
    public static explicit operator System.Security.Principal.WindowsIdentity(Test.WindowsUser from)
    {
        return Test.GlobalMappers.MapToWindowsIdentity(from);
    }
}
```