#if DEBUG
[assembly: RogueGen.Mapping.Attributes.Map<
    RogueGen.UnitTests.WindowsUser,
    System.Security.Principal.WindowsIdentity>(
        ignore: RogueGen.Mapping.Constants.Ignore.OnSource
    )]
#endif
//[assembly: 
//    RogueGen.Mapping.Attributes.Map<
//        RogueGen.UnitTests.IUser,
//        RogueGen.UnitTests.UserDto>]