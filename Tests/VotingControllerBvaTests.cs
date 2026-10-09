using AmarTools.Voting.Controllers;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  BOUNDARY VALUE ANALYSIS (BVA)   -   module: VotingController.CastVote
//
//  Two bounded inputs are analysed:
//    1. The voting window      StartTime <= now < EndTime    (start inclusive, end EXCLUSIVE)
//    2. candidateId            must be one of the program's candidates (here 10 and 11)
//
//  The controller reads the clock with DateTime.UtcNow, so an exact "now == EndTime" cannot be
//  produced through the controller. The exact boundary points are therefore verified on the rule
//  the controller delegates to (VotingProgram.IsOpenAt, BVA-01..05) and the controller itself is
//  checked 10 s inside / 1 s outside the boundary (BVA-06..08).
// ─────────────────────────────────────────────────────────────────────────────
[Trait("Technique", "BVA")]
public sealed class VotingControllerBvaTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddHours(1);

    // BVA-01 .. BVA-05  exact boundary points of the voting window
    [Theory]
    [InlineData(-1, false)]     // BVA-01  1 s before StartTime
    [InlineData(0, true)]       // BVA-02  exactly StartTime  (inclusive)
    [InlineData(3599, true)]    // BVA-03  1 s before EndTime
    [InlineData(3600, false)]   // BVA-04  exactly EndTime    (exclusive)
    [InlineData(3601, false)]   // BVA-05  1 s after EndTime
    public void BVA_01_to_05_IsOpenAt_ExactWindowBoundaries(int secondsFromStart, bool expectedOpen)
    {
        var program = new VotingProgram { IsPublished = true, StartTime = Start, EndTime = End };

        Assert.Equal(expectedOpen, program.IsOpenAt(Start.AddSeconds(secondsFromStart)));
    }

    // BVA-06  voting starts in 10 s  -> still closed -> vote rejected
    [Fact]
    public async Task BVA_06_CastVote_VotingStartsInTenSeconds_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: 10, endOffsetSeconds: 3600);
        h.AddVoter();

        var result = await h.Controller.CastVote(VotingHarness.ProgramId, VotingHarness.CandidateA);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Contains("no longer active", h.Error);
        Assert.Empty(h.Db.Votes);
    }

    // BVA-07  voting ends in 10 s  -> still open -> vote accepted
    [Fact]
    public async Task BVA_07_CastVote_VotingEndsInTenSeconds_IsAccepted()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: -3600, endOffsetSeconds: 10);
        h.AddVoter();

        var result = await h.Controller.CastVote(VotingHarness.ProgramId, VotingHarness.CandidateA);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(VotingController.ThankYou), redirect.ActionName);
        Assert.Single(h.Db.Votes);
    }

    // BVA-08  voting ended 1 s ago -> closed -> vote rejected
    [Fact]
    public async Task BVA_08_CastVote_VotingEndedOneSecondAgo_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: -3600, endOffsetSeconds: -1);
        h.AddVoter();

        var result = await h.Controller.CastVote(VotingHarness.ProgramId, VotingHarness.CandidateA);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Contains("no longer active", h.Error);
        Assert.Empty(h.Db.Votes);
    }

    // BVA-09  candidateId around the valid ids {10, 11}
    [Theory]
    [InlineData(9, false)]              // valid minimum - 1
    [InlineData(10, true)]              // valid minimum
    [InlineData(11, true)]              // valid maximum
    [InlineData(12, false)]             // valid maximum + 1
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    public async Task BVA_09_CastVote_CandidateIdBoundaries(int candidateId, bool valid)
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();

        var result = await h.Controller.CastVote(VotingHarness.ProgramId, candidateId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        if (valid)
        {
            Assert.Equal(nameof(VotingController.ThankYou), redirect.ActionName);
            Assert.Single(h.Db.Votes);
        }
        else
        {
            Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
            Assert.Contains("Invalid candidate", h.Error);
            Assert.Empty(h.Db.Votes);
        }
    }

    // Extra: the same exact boundaries hold when the voting page itself is rendered.
    [Fact]
    public async Task Extra_Vote_PageIsClosedWhenVotingEndedOneSecondAgo()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: -3600, endOffsetSeconds: -1);

        var view = Assert.IsType<ViewResult>(await h.Controller.Vote(VotingHarness.ProgramId));

        Assert.Equal("Closed", view.ViewName);
    }
}
