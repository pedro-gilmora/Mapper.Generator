using SourceCrafter.Mapping.Attributes;
using SourceCrafter.UnitTests;

[assembly: 
    Map<WindowsUser, UserDto>,
    Map<WindowsUser, User>,
    Map<User, UserDto>]