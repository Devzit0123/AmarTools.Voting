using AmarTools.Voting.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  DEFECT TESTS for VotingController.
//
//  Every test here asserts the behaviour the application SHOULD have. A test that fails is a
//  confirmed defect (it is the evidence for the matching entry in VotingControllerDefectReports.md);
//  a test that passes means the defect is not reproducible and its report is set to "Not reproducible".
//
//  Run only these:   dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category=Defect"
//  Run all others:   dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category!=Defect"
//
//  DV-01 (Search cannot translate IsActive) is test BP_07 in VotingControllerBasisPathTests.cs.
// ─────────────────────────────────────────────────────────────────────────────
[Trait("Category", "Defect")]
public sealed class VotingControllerDefectTests
{
    private const int Program1 = VotingHarness.ProgramId;

    // DV-02  Draft (unpublished) program is visible to anyone through /Voting/Vote/{id}.
    //        PublicResults hides drafts with 404; Vote should do the same.
    [Fact]
    public async Task DV02_Vote_UnpublishedProgram_ShouldNotBeDisclosed()
    {
        using var h = new VotingHarness(authenticated: false);
        h.AddProgram(published: false, name: "Secret Draft Election");

        var result = await h.Controller.Vote(Program1);

        Assert.IsType<NotFoundResult>(result);
    }

    // Owner-only registration means a user with an empty FullName cannot self-register.
    [Fact]
    public async Task DV03_Join_UserWithEmptyFullName_ShouldNotSelfRegister()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddUser("voter-1", "bob@example.com", fullName: "", email: "bob@example.com");

        var result = await h.Controller.Join(Program1);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(h.Db.Voters);
    }

    // DV-04  A signed-in principal WITHOUT a NameIdentifier claim matches every voter whose UserId is NULL
    //        (voters added by the owner who have no account yet) and is shown the ballot.
    [Fact]
    public async Task DV04_Vote_PrincipalWithoutUserId_ShouldNotMatchOwnerRegisteredVoters()
    {
        using var h = new VotingHarness(userId: null);
        h.AddProgram();
        h.AddVoter(userId: null);          // owner-registered voter without an account

        await h.Controller.Vote(Program1);

        Assert.False((bool)h.Controller.ViewBag.IsRegisteredVoter);
    }

    // DV-05  The User-Agent is stored unbounded; the column is limited to 512 characters
    //        (PostgreSQL rejects longer values, so such a voter could not vote at all).
    [Fact]
    public async Task DV05_CastVote_VeryLongUserAgent_ShouldBeLimitedTo512Characters()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();
        h.Controller.HttpContext.Request.Headers["User-Agent"] = new string('x', 600);

        await h.Controller.CastVote(Program1, VotingHarness.CandidateA);

        var vote = Assert.Single(h.Db.Votes);
        Assert.True(vote.UserAgent!.Length <= 512, $"Stored {vote.UserAgent.Length} characters.");
    }

    // DV-06  If the blockchain check throws, PublicResults crashes (HTTP 500) instead of showing the
    //        results with "chain could not be verified".
    [Fact]
    public async Task DV06_PublicResults_BlockchainCheckThrows_ShouldStillShowResults()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.Blockchain.Setup(b => b.IsChainValidForProgramAsync(It.IsAny<AmarTools.Voting.Data.VotingDbContext>(), It.IsAny<int>()))
                    .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var error = await Record.ExceptionAsync(() => h.Controller.PublicResults(Program1));

        Assert.Null(error);
    }

    // DV-07  A voter who has ALREADY voted is still shown enabled "Vote" buttons
    //        (the page only knows "registered", not "has voted").
    [Fact]
    public async Task DV07_Vote_VoterWhoAlreadyVoted_ShouldNotBeOfferedTheBallot()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter(hasVoted: true);

        await h.Controller.Vote(Program1);

        var data = h.Controller.ViewData;
        bool ballotOffered = (bool)data["IsRegisteredVoter"]!;
        bool pageKnowsHasVoted = data.ContainsKey("HasVoted") && data["HasVoted"] is true;
        Assert.False(ballotOffered && !pageKnowsHasVoted,
            "The page offers the ballot to a voter who already voted and carries no HasVoted flag.");
    }

    // DV-08  The voting page says "Only the program owner can register voters", yet any signed-in user
    //        who POSTs to /Voting/Join becomes an eligible voter. (Needs a product decision: either
    //        remove Join or fix the page text. The same finding is D16 in the ProgramOwner report.)
    [Fact]
    public async Task DV08_Join_ArbitrarySignedInUser_ShouldNotBecomeAnEligibleVoter()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddUser("voter-1", "stranger@example.com", fullName: "Stranger", email: "stranger@example.com");

        await h.Controller.Join(Program1);

        Assert.Empty(h.Db.Voters);
    }

    // DV-09  /Voting/ThankYou can be opened directly and tells anybody "Your vote has been recorded".
    [Fact]
    public void DV09_ThankYou_WithoutARecentVote_ShouldNotConfirmAVote()
    {
        using var h = new VotingHarness(authenticated: false);

        var result = h.Controller.ThankYou();

        Assert.IsNotType<ViewResult>(result);
    }
}
