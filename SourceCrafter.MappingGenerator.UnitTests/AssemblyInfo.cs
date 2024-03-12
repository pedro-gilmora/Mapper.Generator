using SourceCrafter.Bindings.Attributes;
using SourceCrafter.Bindings.UnitTests;
using SourceCrafter.UnitTests;

[assembly:
    Bind<IImplement<IAppUser, AppUser>, MeAsUser>,
    Bind<User, User>,
    Bind<User, UserDto>]
