using SourceCrafter.Bindings.Attributes;

//Testing utils
using Xunit;

// Analyzer 

//Testing purpose
using FluentAssertions;
using System.ComponentModel;
using static SourceCrafter.EnumExtensions.EnumExtensions;
using SourceCrafter.Bindings.Helpers;
using Newtonsoft.Json.Linq;
using FluentAssertions.Common;

[assembly: Extend<SourceCrafter.Bindings.UnitTests.Status>]

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
        StatusEnum.GetValues().Should().BeEquivalentTo([Status.NotStarted, Status.Stopped, Status.Started, Status.Cancelled, Status.Failed]);

        StatusEnum.GetDescriptions().Should().BeEquivalentTo([NotStartedDesc, StoppedDesc, StartedDesc, CancelledDesc, Failure]);

        StatusEnum.GetNames().Should().BeEquivalentTo(["NotStarted", "Stopped", "Started", "Cancelled", "Failed"]);

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
        Status2Enum.GetValues().Should().BeEquivalentTo([Status2.NotStarted, Status2.Stopped, Status2.Started, Status2.Cancelled, Status2.Failed]);

        Status2Enum.GetDescriptions().Should().BeEquivalentTo([NotStartedDesc, StoppedDesc, StartedDesc, CancelledDesc, Failure]);

        Status2Enum.GetNames().Should().BeEquivalentTo(["NotStarted","Stopped","Started","Cancelled","Failed"]);

        Status2.Started.GetName().Should().Be("Started");

        "Cancelled".TryGetValue(out Status2 status).Should().BeTrue();

        Status2.Cancelled.Should().Be(status);

        "Unknown".TryGetValue(out Status2 _).Should().BeFalse();

        Status2Enum.IsDefined(1).Should().BeTrue();

        Status2Enum.IsDefined("Failed").Should().BeTrue();

        Status2Enum.IsDefined(5).Should().BeFalse();

        Status2Enum.IsDefined("Uknown").Should().BeFalse();

        Status2.Started.TryGetName(out var name).Should().BeTrue();
            
        name.Should().Be("Started");

        ((Status2)6).TryGetName(out _).Should().BeFalse();

        Status2.Cancelled.TryGetDescription(out var desc).Should().BeTrue();

        desc.Should().Be(CancelledDesc);

        ((Status2)6).TryGetDescription(out _).Should().BeFalse();
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

[Extend]
public enum Status2
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
