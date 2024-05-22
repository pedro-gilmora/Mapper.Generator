//Testing utils

// Analyzer 
//using SourceCrafter.Mapping.Attributes;
//using SourceCrafter.Mapping.Constants;

//Testing purpose

using SourceCrafter.Bindings.UnitTests;

namespace SourceCrafter.UnitTests;

//[DefaultMap<User>]
public interface IUser
{
    //[Map(nameof(WindowsUser.Name))]
    string FullName { get; set; }
    int Age { get; set; }
    //[Ignore(Ignore.Source)]
    string Unwanted { get; set; }
    DateTime DateOfBirth { get; set; }
    double Balance { get; set; }
    IEnumerable<IUser> Asignees { get; set; }
    Role? MainRole { get; set; }
    Status Status { get; }
}
