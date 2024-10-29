using SourceCrafter.Bindings.Attributes;

//Testing utils
using Xunit;

// Analyzer 

//Testing purpose
using FluentAssertions;
using System.ComponentModel;
using static SourceCrafter.EnumExtensions.EnumExtensions;
using FluentAssertions.Common;
using SourceCrafter.Bindings.Constants;

[assembly: Extend<MappingKind>]

namespace SourceCrafter.Bindings.UnitTests;

public class EnumsTest
{
    private const string 
        NotStartedDesc = "Not Started",
        StoppedDesc = "Transaction was stopped",
        StartedDesc = "Transaction has been started",
        CancelledDesc = "Transaction has been cancelled by user",
        Failure = "Transaction had an external failure";

    [Fact]
    public void TestEnums()
    {
        StatusEnum.GetValues().ToArray().Should().BeEquivalentTo([Status.NotStarted, Status.Stopped, Status.Started, Status.Cancelled, Status.Failed]);

        StatusEnum.GetDescriptions().ToArray().Should().BeEquivalentTo([NotStartedDesc, StoppedDesc, StartedDesc, CancelledDesc, Failure]);

        StatusEnum.GetNames().ToArray().Should().BeEquivalentTo(["NotStarted", "Stopped", "Started", "Cancelled", "Failed"]);

        Status.Started.GetName().Should().Be("Started");

        "Cancelled".TryGetValue(out Status status).Should().BeTrue();

        Status.Cancelled.Should().Be(status);

        "Unknown".TryGetValue(out Status _).Should().BeFalse();

        StatusEnum.IsDefined(1).Should().BeTrue();

        StatusEnum.IsDefined("Failed").Should().BeTrue();

        StatusEnum.IsDefined(5).Should().BeFalse();

        StatusEnum.IsDefined("Uknown").Should().BeFalse();

        Status.Started.TryGetName(out var name).Should().BeTrue();

        name.Should().Be("Started");

        ((Status)6).TryGetName(out _).Should().BeFalse();

        Status.Cancelled.TryGetDescription(out var desc).Should().BeTrue();

        desc.Should().Be(CancelledDesc);

        ((Status)6).TryGetDescription(out _).Should().BeFalse();
    }
    [Fact]
    public void TestAssemblyEnums()
    {
        MappingKindEnum.GetDescriptions().ToArray().Should().BeEquivalentTo(["All", "Normal", "Fill"]);

        MappingKindEnum.GetNames().ToArray().Should().BeEquivalentTo(["All", "Normal", "Fill"]);

        MappingKind.Fill.GetName().Should().Be("Fill");

        "Fill".TryGetValue(out MappingKind kind).Should().BeTrue();

        MappingKind.Fill.Should().Be(kind);

        "Unknown".TryGetValue(out MappingKind _).Should().BeFalse();

        MappingKindEnum.IsDefined(1).Should().BeTrue();

        MappingKindEnum.IsDefined("Fill").Should().BeTrue();

        MappingKindEnum.IsDefined(5).Should().BeFalse();

        MappingKindEnum.IsDefined("Uknown").Should().BeFalse();

        MappingKind.Fill.TryGetName(out var name).Should().BeTrue();

        name.Should().Be("Fill");

        ((MappingKind)6).TryGetName(out _).Should().BeFalse();

        MappingKind.Fill.TryGetDescription(out var desc).Should().BeTrue();

        desc.Should().Be("Fill");

        ((MappingKind)6).TryGetDescription(out _).Should().BeFalse();
    }
}

[Extend]
public enum Status
{
    NotStarted,
    [Description("Transaction was stopped")]
    Stopped,
    [Description("Transaction has been started")]
    Started,
    [Description("Transaction has been cancelled by user")]
    Cancelled,
    [Description("Transaction had an external failure")]
    Failed
}