using SourceCrafter.Bindings.Attributes;
using SourceCrafter.MappingGenerator.UnitTests;
using SourceCrafter.UnitTests;

[assembly:
    Bind<IImplement<IAppUser, AppUser>, MeAsUser>,
    Bind<User, User>,
    Bind<User, UserDto>]
