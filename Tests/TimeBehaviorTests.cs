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
    public void NormalizeToUtc_ConvertsBangladeshBrowserOffset()
    {
        var localValue = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = VotingService.NormalizeToUtc(localValue, 360);

        Assert.Equal(new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Unspecified), result);
    }

    [Fact]
    public void NormalizeToUtc_ConvertsUsEasternBrowserOffset()
    {
        var localValue = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = VotingService.NormalizeToUtc(localValue, -300);

        Assert.Equal(new DateTime(2026, 1, 1, 17, 0, 0, DateTimeKind.Unspecified), result);
    }

    [Fact]
    public void NormalizeToUtc_ThrowsWhenBrowserOffsetIsMissing()
    {
        var localValue = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => VotingService.NormalizeToUtc(localValue, null));
    }

    [Fact]
    public void CreatedProgram_IsOpenAtStartAndClosedAtEnd()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var program = new VotingProgram
        {
            ProgramName = "Campus Election",
            StartTime = start,
            EndTime = start.AddHours(1),
            IsPublished = true
        };

        Assert.True(program.IsOpenAt(start));
        Assert.False(program.IsOpenAt(program.EndTime));
    }
}
