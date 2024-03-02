using SourceCrafter.Bindings.Attributes;
using SourceCrafter.UnitTests;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Security.Claims;

[assembly:
    Bind<WindowsUser, UserDto>,
    Bind<WindowsUser, User>,
    Bind<User, IUser>,
    Bind<User, User>,
    Bind<User, UserDto>]

namespace Test;

//[Mapper]
public static partial class Mappings
{

    
}

