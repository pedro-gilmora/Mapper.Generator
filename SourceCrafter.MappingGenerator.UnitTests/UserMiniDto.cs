//using SourceCrafter.Mapping.Attributes;

using SourceCrafter.Mappify.Attributes;

namespace SourceCrafter.UnitTests;

[Map<User>]
public class UserMiniDto
{
    public string FullName { get; set; } = null!;
    public int Count { get; set; }
    public int Age { get; set; }
}
