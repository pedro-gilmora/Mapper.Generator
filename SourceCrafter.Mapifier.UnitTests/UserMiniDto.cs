﻿//using SourceCrafter.Mapping.Attributes;

namespace SourceCrafter.UnitTests;

//[Map<User>]
public partial class UserMiniDto
{
    public string FullName { get; set; } = null!;
    public int Count { get; set; }
    //[Ignore]
    public int Age { get; set; }
}
