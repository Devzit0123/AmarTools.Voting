using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Xunit;

namespace AmarTools.Voting.Tests;

public class TimeBehaviorTests
{
    [Fact]
    public void IsOpenAt_IsTrueAtStart()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);

        Assert.True(VotingProgram.IsOpenAt(start, end, start));
    }

    [Fact]
    public void IsOpenAt_IsFalseAtEnd()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);

        Assert.False(VotingProgram.IsOpenAt(start, end, end));
    }

    [Fact]
    public void NormalizeToUtc_ConvertsPositiveSixHourLocalOffset()
    {
        var localValue = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = VotingService.NormalizeToUtc(localValue, -360);

        Assert.Equal(new DateTime(2026, 1, 1, 18, 0, 0, DateTimeKind.Unspecified), result);
    }

    [Fact]
    public void NormalizeToUtc_ConvertsUtcMinusFiveBrowserOffset()
    {
        var localValue = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = VotingService.NormalizeToUtc(localValue, 300);

        Assert.Equal(new DateTime(2026, 1, 1, 7, 0, 0, DateTimeKind.Unspecified), result);
    }
}
