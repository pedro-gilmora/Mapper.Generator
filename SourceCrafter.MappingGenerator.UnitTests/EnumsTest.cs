//Testing utils
using Xunit;

// Analyzer 

//Testing purpose
using FluentAssertions;
using SourceCrafter.Bindings.Attributes;
using System.ComponentModel;
using static SourceCrafter.EnumExtensions.EnumExtensions;
using SourceCrafter.Bindings.Helpers;

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
        GetValues<IEnum<Status>>().Should().BeEquivalentTo([Status.NotStarted, Status.Stopped, Status.Started, Status.Cancelled, Status.Failed]);

        GetDescriptions<IEnum<Status>>().Should().BeEquivalentTo([NotStartedDesc, StoppedDesc, StartedDesc, CancelledDesc, Failure]);

        GetNames<IEnum<Status>>().Should().BeEquivalentTo(["NotStarted","Stopped","Started","Cancelled","Failed"]);

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

public interface IEnumExt<T> where T : Enum
{
}

public interface IEnum2 : IEnumExt<Status>
{
    public static string[] GetNames()
    {
        return ["1"];
    }
}

