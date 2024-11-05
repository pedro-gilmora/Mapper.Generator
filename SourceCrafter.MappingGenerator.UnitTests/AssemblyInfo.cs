using SourceCrafter.Bindings;
using SourceCrafter.Bindings.Attributes;
using SourceCrafter.Bindings.UnitTests;
using SourceCrafter.UnitTests;

[assembly:
    //Bind<(string, object)[], Dictionary<string, string>>,
    Bind<IImplement<IAppUser, AppUser>, MeAsUser>,
    Bind<User, UserDto>
]
