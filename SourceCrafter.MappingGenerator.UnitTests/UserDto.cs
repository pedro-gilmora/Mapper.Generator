//Testing utils

// Analyzer 

//Testing purpose

using SourceCrafter.Mapping.Attributes;

namespace SourceCrafter.UnitTests;

public partial class UserDto
{
    //[Map(nameof(WindowsUser.Name))]
    public string FullName { get; set; } = null!;
    public int Count { get; set; }
    public int Age { get; set; }
    [Ignore]
    public string? Unwanted { get; set; }
    public DateTime DateOfBirth { get; set; }
    public IEnumerable<UserDto?>? Asignees { get; set; }
    public (int, string)? MainRole { get; set; }
    public decimal TotalAmount { get; set; }
    public UserDto? Supervisor { get; init; }
}
