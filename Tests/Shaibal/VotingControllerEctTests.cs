using AmarTools.Voting.Controllers;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  EQUIVALENCE CLASS TESTING (ECT)
//
//  CastVote inputs and their classes (one representative value per class):
//
//    Caller identity   EC1 signed in WITH NameIdentifier (valid)   EC2 signed in WITHOUT claim (invalid)
//    Program state     EC3 published and inside window  (valid)    EC4 unpublished   EC5 upcoming   EC6 ended
//    Voter state       EC7 registered, not voted        (valid)    EC8 not registered  EC9 already voted
//    candidateId       EC10 candidate of this program   (valid)    EC11 candidate of ANOTHER program
//
//  ECT-01..ECT-08 are the eight designed cases; the "Extra" tests cover the classes of the
//  other actions (Join, Search, Vote, PublicResults).
// ─────────────────────────────────────────────────────────────────────────────
[Trait("Technique", "ECT")]
public sealed class VotingControllerEctTests
{
    private const int Program1 = VotingHarness.ProgramId;
    private const int CandA = VotingHarness.CandidateA;

    // ECT-01  EC2: signed in but no NameIdentifier claim -> 401
    [Fact]
    public async Task ECT_01_CastVote_NoNameIdentifierClaim_IsUnauthorized()
    {
        using var h = new VotingHarness(userId: null);
        h.AddProgram();

        Assert.IsType<UnauthorizedResult>(await h.Controller.CastVote(Program1, CandA));
    }

    // ECT-02  EC4: unpublished program -> rejected
    [Fact]
    public async Task ECT_02_CastVote_UnpublishedProgram_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram(published: false);
        h.AddVoter();

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("no longer active", h.Error);
        Assert.Empty(h.Db.Votes);
    }

    // ECT-03  EC5: upcoming program (starts in 2 h) -> rejected
    [Fact]
    public async Task ECT_03_CastVote_UpcomingProgram_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: 7200, endOffsetSeconds: 10800);
        h.AddVoter();

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("no longer active", h.Error);
        Assert.Empty(h.Db.Votes);
    }

    // ECT-04  EC6: ended program (ended 2 h ago) -> rejected
    [Fact]
    public async Task ECT_04_CastVote_EndedProgram_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: -10800, endOffsetSeconds: -7200);
        h.AddVoter();

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("no longer active", h.Error);
        Assert.Empty(h.Db.Votes);
    }

    // ECT-05  EC8: signed-in user is not a registered voter -> rejected
    [Fact]
    public async Task ECT_05_CastVote_UserNotRegisteredAsVoter_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram();

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("not registered", h.Error);
        Assert.Empty(h.Db.Votes);
    }

    // ECT-06  EC9: voter has already voted -> rejected, vote count unchanged
    [Fact]
    public async Task ECT_06_CastVote_VoterAlreadyVoted_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter(hasVoted: true);

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("already cast", h.Error);
        Assert.Empty(h.Db.Votes);
    }

    // ECT-07  EC11: candidate belongs to ANOTHER program -> rejected
    [Fact]
    public async Task ECT_07_CastVote_CandidateOfAnotherProgram_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram(id: 1);
        h.AddProgram(id: 2);            // candidates 20 and 21
        h.AddVoter(programId: 1);

        await h.Controller.CastVote(Program1, 20);

        Assert.Contains("Invalid candidate", h.Error);
        Assert.Empty(h.Db.Votes);
    }

    // ECT-08  EC1 + EC3 + EC7 + EC10: every input valid -> vote recorded
    [Fact]
    public async Task ECT_08_CastVote_AllInputsValid_RecordsVote()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        var voter = h.AddVoter();

        var result = await h.Controller.CastVote(Program1, CandA);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(VotingController.ThankYou), redirect.ActionName);

        var vote = Assert.Single(h.Db.Votes);
        Assert.Equal(CandA, vote.CandidateId);
        Assert.Equal(voter.Id, vote.VoterId);
        Assert.Equal("web", vote.VoteSource);

        var saved = h.Db.Voters.Single();
        Assert.True(saved.HasVoted);
        Assert.NotNull(saved.VotedAt);
    }

    // ───────────────────────── Extra classes (not in the 30 designed cases) ─────────────────────────

    // Join: signed in WITHOUT claim (EC2) -> 401
    [Fact]
    public async Task Extra_Join_NoNameIdentifierClaim_IsUnauthorized()
    {
        using var h = new VotingHarness(userId: null);
        h.AddProgram();

        Assert.IsType<UnauthorizedResult>(await h.Controller.Join(Program1));
    }

    // Join: unpublished program -> registration closed
    [Fact]
    public async Task Extra_Join_UnpublishedProgram_RegistrationIsClosed()
    {
        using var h = new VotingHarness();
        h.AddProgram(published: false);

        await h.Controller.Join(Program1);

        Assert.Contains("closed", h.Error);
        Assert.Empty(h.Db.Voters);
    }

    // Join: program that has not started yet -> only the program owner can register voters.
    [Fact]
    public async Task Extra_Join_UpcomingProgram_RejectsSelfRegistration()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: 7200, endOffsetSeconds: 10800);

        await h.Controller.Join(Program1);

        Assert.Contains("Only the program owner", h.Error);
        Assert.Empty(h.Db.Voters);
    }

    // Search: blank / whitespace / null classes all return an empty JSON array
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task Extra_Search_BlankQueryClass_ReturnsEmptyArray(string? query)
    {
        using var h = new VotingHarness();
        h.AddProgram();

        var json = Assert.IsType<JsonResult>(await h.Controller.Search(query!));

        Assert.Empty((System.Collections.IEnumerable)json.Value!);
    }

    // PublicResults: unpublished program -> 404 (results are hidden for drafts)
    [Fact]
    public async Task Extra_PublicResults_UnpublishedProgram_IsNotFound()
    {
        using var h = new VotingHarness();
        h.AddProgram(published: false);

        Assert.IsType<NotFoundResult>(await h.Controller.PublicResults(Program1));
    }

    // Vote: signed-in registered voter vs signed-in non-registered user
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Extra_Vote_RegisteredFlagFollowsVoterRegistration(bool registered)
    {
        using var h = new VotingHarness();
        h.AddProgram();
        if (registered) h.AddVoter();

        Assert.IsType<ViewResult>(await h.Controller.Vote(Program1));

        Assert.Equal(registered, (bool)h.Controller.ViewBag.IsRegisteredVoter);
    }
}
