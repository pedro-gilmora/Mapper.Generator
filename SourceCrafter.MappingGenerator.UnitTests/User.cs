//using SourceCrafter.Mapping.Attributes;
//using SourceCrafter.Mapping.Constants;

using SourceCrafter.Bindings.Attributes;
using System.Collections.ObjectModel;

namespace SourceCrafter.UnitTests;

public partial class User //: IUser
{
    internal string FullName
    {
        get => $"{LastName?.Trim()}, {FirstName?.Trim()}";
        set => 
            (FirstName, LastName) = value?.Split(", ") switch
            {
                [{ } lastName, { } firstName] => (firstName.Trim(), lastName.Trim()),
                [{ } firstName] => (firstName, null!),
                _ => (null!, null!)
            };
    }
    public string FirstName { get; set; } = null!;
    public string? LastName { get; set; }
    public int Age { get; set; }
    //string IUser.FullName { get => FullName; set => FullName = value; }
    public string? Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
    [Bind(nameof(UserDto.TotalAmount))]
    public double Balance { get; set; }
    [Max(2)]
    public User[] Asignees { get; set; } = [];
    public Role MainRole { get; set; }
    public User? Supervisor { get; init; }
    public string[] Phrases { get; set; } = [];
}


public partial class User
{
    public int Count { get; set; }

}