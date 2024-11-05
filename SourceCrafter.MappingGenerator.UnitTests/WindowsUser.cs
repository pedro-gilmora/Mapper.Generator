using SourceCrafter.Bindings.Attributes;
using SourceCrafter.Bindings.Constants;

namespace SourceCrafter.UnitTests;

public partial class WindowsUser
{
    [Bind(nameof(User.FullName), IgnoreBind.Source)]
    [Bind(nameof(UserDto.FullName))]
    public string Name { get; set; } = null!;
    public bool IsAuthenticated { get; set; }
}
