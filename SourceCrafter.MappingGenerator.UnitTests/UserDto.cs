﻿//Testing utils

// Analyzer 

//Testing purpose

//using SourceCrafter.Mapping.Attributes;

using SourceCrafter.Bindings.Attributes;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml;

namespace SourceCrafter.UnitTests;

public partial class UserDto
{
    public string FullName { get; set; } = null!;

    public int Count { get; set; }

    public int Age { get; set; }

    [IgnoreBind]
    public string? Unwanted { get; set; }

    public DateTime DateOfBirth { get; set; }

    //public IEnumerable<UserDto?> Asignees = [];
    public (int id, string name) MainRole { get; set; }

    public decimal TotalAmount { get; set; }
    public Dictionary<string, string> ExtendedProperties { get; set; } = [];

    //public UserDto? Supervisor { get; init; }
    public IEnumerable<string> Phrases { get; set; } = Array.Empty<string>();

}
