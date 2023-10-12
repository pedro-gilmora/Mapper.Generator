//Testing utils

// Analyzer 

//Testing purpose

namespace SourceCrafter.UnitTests;

public partial class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public static explicit operator Role((int id, string name) role) 
        => new() { Id = role.id, Name = role.name };
    
    public static explicit operator Role?((int id, string name)? role)
        => role is { } a ? new() { Id = a.id, Name = a.name } : null;
    
    public static explicit operator (int id, string name)(Role role) 
        => (role.Id, role.Name);
    
    public static explicit operator (int id, string name)?(Role? role)
        => role != null ? (role.Id, role.Name) : null;
}
