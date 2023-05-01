#if DEBUG
[assembly: SourceCrafter.Mapping.Attributes.Map<
    SourceCrafter.UnitTests.WindowsUser,
    System.Security.Principal.WindowsIdentity>(
        ignore: SourceCrafter.Mapping.Constants.Ignore.OnSource
    )]
#endif
//[assembly: 
//    SourceCrafter.Mapping.Attributes.Map<
//        SourceCrafter.UnitTests.IUser,
//        SourceCrafter.UnitTests.UserDto>]