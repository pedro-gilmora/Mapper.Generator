using SourceCrafter.Bindings.Attributes;
using SourceCrafter.UnitTests;

[assembly:
    Bind<IUser, UserDto>,
    Bind<IUser, User>,
    Bind<User, User>,
    Bind<User, UserDto>]
