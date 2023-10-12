using SourceCrafter.Mapping.Attributes;

namespace SourceCrafter.UnitTests;

public partial class WindowsUser
{
    [Map(nameof(User.FullName))]
    public string Name { get; set; } = null!;
    public bool IsAuthenticated { get; set; }
}

public class TestA { public int Age { get; set; } }
public class TestB { public int Age{get;set;} }
