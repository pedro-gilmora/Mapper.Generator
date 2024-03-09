using SourceCrafter.Bindings.Attributes;
using SourceCrafter.MappingGenerator.UnitTests;
using SourceCrafter.UnitTests;

[assembly:
    Bind<IUser, UserDto>,
    Bind<IUser, User>,
    Bind<User, User>,
    Bind<AppUser, MeAsUser>,
    Bind<User, UserDto>]
