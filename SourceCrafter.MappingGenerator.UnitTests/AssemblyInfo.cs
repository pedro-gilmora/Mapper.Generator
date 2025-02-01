using SourceCrafter.Mappify.Attributes;
using SourceCrafter.Mappify.UnitTests;
using SourceCrafter.UnitTests;

[assembly:
    //Bind<(string, object)[], Dictionary<string, string>>,
    Map<IAppUser, MeAsUser>,
    Map<User, UserDto>
]
