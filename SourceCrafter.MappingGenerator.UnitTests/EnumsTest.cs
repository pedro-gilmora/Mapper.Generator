//Testing utils
using Xunit;
using System.Text;

// Analyzer 
using SourceCrafter.Bindings;

//Testing purpose
using FluentAssertions;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceCrafter.Bindings.Attributes;
using System.ComponentModel;
using static SourceCrafter.EnumExtensions.EnumExtensions;
using SourceCrafter.Bindings.Helpers;
using SourceCrafter.Bindings.Constants;

namespace SourceCrafter.Bindings.UnitTests;

public class EnumsTest
{
    private const string 
        NotStartedDesc = "Not Started",
        StoppedDesc = "Transaction was stopped",
        StartedDesc = "Transaction has been started",
        CancelledDesc = "Transaction has been cancelled",
        Failure = "Transaction had a failure";

    [Fact]
    public void TestEnums()
    {
        GetStatusDescriptions().Should().BeEquivalentTo([NotStartedDesc, StoppedDesc, StartedDesc, CancelledDesc, Failure]);

        Status.Started.GetName().Should().Be("Started");

        "Cancelled".TryGetValue(out var status).Should().BeTrue();

        Status.Cancelled.Should().Be(status);

        "Unknown".TryGetValue(out _).Should().BeFalse();

        1.IsDefined<IEnum<Status>>().Should().BeTrue();

        "Failed".IsDefined<IEnum<Status>>().Should().BeTrue();

        5.IsDefined<IEnum<Status>>().Should().BeFalse();

        "Uknown".IsDefined<IEnum<Status>>().Should().BeFalse();

        Status.Started.TryGetName(out var name).Should().BeTrue();
            
        name.Should().Be("Started");

        ((Status)6).TryGetName(out _).Should().BeFalse();

        Status.Cancelled.TryGetDescription(out var desc).Should().BeTrue();

        desc.Should().Be(CancelledDesc);

        ((Status)6).TryGetDescription(out _).Should().BeFalse();
    }
}

[Extend((nameof(Started)+nameof(ApplyOn.Source)) + (nameof(Cancelled) + nameof(ApplyOn.Target)))]
public enum Status
{
    NotStarted,
    [Description("Transaction was stopped")]
    Stopped,
    [Description("Transaction has been started")]
    Started,
    [Description("Transaction has been cancelled")]
    Cancelled,
    [Description("Transaction had a failure")]
    Failed
}