using SourceCrafter.Mapping.Attributes;
using SourceCrafter.Mapping.Constants;

namespace SourceCrafter.UnitTests;

public partial class User //: IUser
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
    //string IUser.FullName { get => FullName; set => FullName = value; }
    public string? Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
    [Map(nameof(UserDto.TotalAmount))]
    public double Balance { get; set; }
    public IEnumerable<User?>? Asignees { get; set; }
    public Role? MainRole { get; set; }
    public User? Supervisor { get; init; }
}


public partial class User
{
    public int Count { get; set; }

}