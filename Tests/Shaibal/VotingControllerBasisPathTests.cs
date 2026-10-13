using System.Security.Claims;
using AmarTools.Voting.Controllers;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  BASIS PATH TESTING   -   five modules of VotingController
//
//    Module            decisions                                             V(G)   feasible paths
//    Vote (GET)        null, !IsOpenAt, ternary, IsAuthenticated              5      4  (ternary else-branch is dead code)
//    PublicResults     null, !IsPublished                                     3      3
//    Search            IsNullOrWhiteSpace                                     2      2
//    Join              userId, null, !IsPublished, now>=End, exists, catch    7      7
//    CastVote          userId, null, !IsOpenAt, voter null, HasVoted,         12     12 (P12 is in VotingControllerNpgsqlTests)
//                      candidate null, queued, 3 catch / unique-violation tests
//
//  BP-01..BP-13 are the 13 designed cases. The remaining paths are automated as "Px" tests.
// ─────────────────────────────────────────────────────────────────────────────

// ═════════════════════════════ Module 1: Vote (GET) ═════════════════════════════
[Trait("Technique", "BasisPath")]
public sealed class VotingControllerBasisPath_Vote
{
    // BP-01  P1: program == null -> 404
    [Fact]
    public async Task BP_01_Vote_ProgramMissing_IsNotFound()
    {
        using var h = new VotingHarness();

        Assert.IsType<NotFoundResult>(await h.Controller.Vote(404));
    }

    // BP-02  P2: program not open -> "Closed" view with message
    [Fact]
    public async Task BP_02_Vote_ProgramEnded_ReturnsClosedView()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: -7200, endOffsetSeconds: -60);

        var view = Assert.IsType<ViewResult>(await h.Controller.Vote(VotingHarness.ProgramId));

        Assert.Equal("Closed", view.ViewName);
        Assert.IsType<VotingProgram>(view.Model);
        Assert.Equal("This voting program is not currently active.", (string)h.Controller.ViewBag.Message);
    }

    // BP-03  P3: open program + anonymous visitor -> voting page, not a registered voter
    [Fact]
    public async Task BP_03_Vote_OpenProgram_Anonymous_ShowsPageWithoutRegistration()
    {
        using var h = new VotingHarness(authenticated: false);
        h.AddProgram();

        var view = Assert.IsType<ViewResult>(await h.Controller.Vote(VotingHarness.ProgramId));

        Assert.Null(view.ViewName);
        Assert.False((bool)h.Controller.ViewBag.IsRegisteredVoter);
        var remaining = (TimeSpan?)h.Controller.ViewBag.RemainingTime;
        Assert.True(remaining > TimeSpan.Zero);
    }

    // P4: open program + signed-in registered voter -> IsRegisteredVoter == true
    [Fact]
    public async Task P4_Vote_OpenProgram_SignedInRegisteredVoter_IsRegistered()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();

        await h.Controller.Vote(VotingHarness.ProgramId);

        Assert.True((bool)h.Controller.ViewBag.IsRegisteredVoter);
    }

    // P3b: principal that has no identity at all is treated as anonymous (null-conditional branch)
    [Fact]
    public async Task P3b_Vote_PrincipalWithoutIdentity_IsTreatedAsAnonymous()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.Controller.HttpContext.User = new ClaimsPrincipal();

        await h.Controller.Vote(VotingHarness.ProgramId);

        Assert.False((bool)h.Controller.ViewBag.IsRegisteredVoter);
    }

    // The candidates are sorted by name before they are shown
    [Fact]
    public async Task Extra_Vote_CandidatesAreSortedByName()
    {
        using var h = new VotingHarness();
        h.AddProgram(candidateCount: 4);   // Alice, Bob, Carol, Dave

        await h.Controller.Vote(VotingHarness.ProgramId);

        var names = ((List<Candidate>)h.Controller.ViewBag.Candidates).Select(c => c.Name).ToList();
        Assert.Equal(names.OrderBy(n => n).ToList(), names);
    }
}

// ═════════════════════════════ Module 2: PublicResults ═════════════════════════════
[Trait("Technique", "BasisPath")]
public sealed class VotingControllerBasisPath_PublicResults
{
    // BP-04  P1: program == null -> 404
    [Fact]
    public async Task BP_04_PublicResults_ProgramMissing_IsNotFound()
    {
        using var h = new VotingHarness();

        Assert.IsType<NotFoundResult>(await h.Controller.PublicResults(5));
    }

    // BP-05  P3: published program -> results view with total and chain status
    [Fact]
    public async Task BP_05_PublicResults_PublishedProgram_ReturnsViewWithTotals()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.Service.Setup(s => s.GetResultsAsync(VotingHarness.ProgramId)).ReturnsAsync(new List<CandidateResultViewModel>
        {
            new() { Candidate = new Candidate { Id = 10, Name = "Alice", CandidateCode = "A" }, VoteCount = 3 },
            new() { Candidate = new Candidate { Id = 11, Name = "Bob",   CandidateCode = "B" }, VoteCount = 2 },
        });

        var view = Assert.IsType<ViewResult>(await h.Controller.PublicResults(VotingHarness.ProgramId));

        Assert.Equal("PublicResults", view.ViewName);
        Assert.Equal(5, (int)h.Controller.ViewBag.TotalVotes);
        Assert.True((bool)h.Controller.ViewBag.BlockchainValid);
    }

    // P2: unpublished program -> 404
    [Fact]
    public async Task P2_PublicResults_UnpublishedProgram_IsNotFound()
    {
        using var h = new VotingHarness();
        h.AddProgram(published: false);

        Assert.IsType<NotFoundResult>(await h.Controller.PublicResults(VotingHarness.ProgramId));
    }

    // A broken chain is reported to the view, not hidden
    [Fact]
    public async Task Extra_PublicResults_TamperedChain_IsReportedAsInvalid()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.Blockchain.Setup(b => b.IsChainValidForProgramAsync(It.IsAny<AmarTools.Voting.Data.VotingDbContext>(), VotingHarness.ProgramId))
                    .ReturnsAsync(false);

        await h.Controller.PublicResults(VotingHarness.ProgramId);

        Assert.False((bool)h.Controller.ViewBag.BlockchainValid);
    }
}

// ═════════════════════════════ Module 3: Search ═════════════════════════════
[Trait("Technique", "BasisPath")]
public sealed class VotingControllerBasisPath_Search
{
    // BP-06  P1: blank query -> empty JSON array (no database access)
    [Fact]
    public async Task BP_06_Search_BlankQuery_ReturnsEmptyArray()
    {
        using var h = new VotingHarness();
        h.AddProgram();

        var json = Assert.IsType<JsonResult>(await h.Controller.Search("   "));

        Assert.Empty((System.Collections.IEnumerable)json.Value!);
    }

    // BP-07  P2: non-blank query -> JSON list with the matching published program.
    // PREDICTED TO FAIL (defect DV-01): the query orders by the [NotMapped] property
    // VotingProgram.IsActive, which EF Core cannot translate to SQL.
    [Fact]
    [Trait("Category", "Defect")]
    public async Task BP_07_Search_ValidQuery_ReturnsMatchingProgram()
    {
        using var h = new VotingHarness();
        h.AddProgram(name: "Student Council Election");

        var json = Assert.IsType<JsonResult>(await h.Controller.Search("council"));

        Assert.Single((System.Collections.IEnumerable)json.Value!);
    }
}

// ═════════════════════════════ Module 4: Join ═════════════════════════════
[Trait("Technique", "BasisPath")]
public sealed class VotingControllerBasisPath_Join
{
    // P1: no NameIdentifier claim -> 401
    [Fact]
    public async Task P1_Join_NoUserId_IsUnauthorized()
    {
        using var h = new VotingHarness(userId: null);

        Assert.IsType<UnauthorizedResult>(await h.Controller.Join(VotingHarness.ProgramId));
    }

    // P2: program missing -> 404
    [Fact]
    public async Task P2_Join_ProgramMissing_IsNotFound()
    {
        using var h = new VotingHarness();

        Assert.IsType<NotFoundResult>(await h.Controller.Join(99));
    }

    // P3: unpublished program -> registration closed
    [Fact]
    public async Task P3_Join_UnpublishedProgram_RegistrationClosed()
    {
        using var h = new VotingHarness();
        h.AddProgram(published: false);

        await h.Controller.Join(VotingHarness.ProgramId);

        Assert.Contains("closed", h.Error);
    }

    // BP-08  P4: program already ended (now >= EndTime) -> registration closed
    [Fact]
    public async Task BP_08_Join_EndedProgram_RegistrationClosed()
    {
        using var h = new VotingHarness();
        h.AddProgram(startOffsetSeconds: -7200, endOffsetSeconds: -60);

        var redirect = Assert.IsType<RedirectToActionResult>(await h.Controller.Join(VotingHarness.ProgramId));

        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Contains("closed", h.Error);
        Assert.Empty(h.Db.Voters);
    }

    // BP-09: self-registration is disabled; voter records are created by program owners.
    [Fact]
    public async Task BP_09_Join_ValidUser_IsRejectedWithoutOwnerRegistration()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddUser("voter-1", "student@example.com", fullName: "Student One", email: "student@example.com");

        var redirect = Assert.IsType<RedirectToActionResult>(await h.Controller.Join(VotingHarness.ProgramId));

        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Contains("Only the program owner", h.Error);
        Assert.Empty(h.Db.Voters);
    }

    // BP-10  P5: already registered -> error, no duplicate row
    [Fact]
    public async Task BP_10_Join_AlreadyRegistered_IsRejectedWithoutDuplicate()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();

        await h.Controller.Join(VotingHarness.ProgramId);

        Assert.Contains("already registered", h.Error);
        Assert.Single(h.Db.Voters);
    }

    // P7: database failure while saving -> friendly error
    [Fact]
    public async Task P7_Join_OpenProgram_RejectsSelfRegistrationWithoutSaving()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        await h.Controller.Join(VotingHarness.ProgramId);

        Assert.Contains("Only the program owner", h.Error);
        Assert.Empty(h.Db.Voters);
    }

    // P6b: an authenticated account without a user row cannot self-register.
    [Fact]
    public async Task P6b_Join_UserRecordMissing_CannotSelfRegister()
    {
        using var h = new VotingHarness();
        h.AddProgram();

        await h.Controller.Join(VotingHarness.ProgramId);

        Assert.Contains("Only the program owner", h.Error);
        Assert.Empty(h.Db.Voters);
    }
}

// ═════════════════════════════ Module 5: CastVote ═════════════════════════════
[Trait("Technique", "BasisPath")]
public sealed class VotingControllerBasisPath_CastVote
{
    private const int Program1 = VotingHarness.ProgramId;
    private const int CandA = VotingHarness.CandidateA;

    // P1 (designed as ECT-01): no NameIdentifier claim -> 401
    [Fact]
    public async Task P01_CastVote_NoUserId_IsUnauthorized()
    {
        using var h = new VotingHarness(userId: null);

        Assert.IsType<UnauthorizedResult>(await h.Controller.CastVote(Program1, CandA));
    }

    // BP-11  P2: program missing -> 404
    [Fact]
    public async Task BP_11_CastVote_ProgramMissing_IsNotFound()
    {
        using var h = new VotingHarness();

        Assert.IsType<NotFoundResult>(await h.Controller.CastVote(404, CandA));
    }

    // P3 (designed as ECT-02/03/04 and BVA-06/08): program not open
    [Fact]
    public async Task P03_CastVote_ProgramNotOpen_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram(published: false);
        h.AddVoter();

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("no longer active", h.Error);
    }

    // P4 (ECT-05): voter == null.   P5 (ECT-06): HasVoted.   P6 (ECT-07 / BVA-09): candidate == null.
    [Fact]
    public async Task P04_CastVote_VoterMissing_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram();

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("not registered", h.Error);
    }

    [Fact]
    public async Task P05_CastVote_AlreadyVoted_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter(hasVoted: true);

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("already cast", h.Error);
    }

    [Fact]
    public async Task P06_CastVote_UnknownCandidate_IsRejected()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();

        await h.Controller.CastVote(Program1, 999);

        Assert.Contains("Invalid candidate", h.Error);
    }

    // BP-12  P7: success and the blockchain block is queued
    [Fact]
    public async Task BP_12_CastVote_Success_BlockQueued_NoWarning()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();

        var redirect = Assert.IsType<RedirectToActionResult>(await h.Controller.CastVote(Program1, CandA));

        Assert.Equal(nameof(VotingController.ThankYou), redirect.ActionName);
        Assert.Null(h.Warning);
        Assert.Equal("Your vote has been recorded successfully!", h.Success);
        Assert.Equal("Test Program 1", h.Controller.TempData["ProgramName"]);
        Assert.Equal(Program1, h.Controller.TempData["ProgramId"]);
        h.Queue.Verify(q => q.TryEnqueue(It.IsAny<int>()), Times.Once);
    }

    // BP-13  P8: success but the queue is full -> vote kept, warning shown
    [Fact]
    public async Task BP_13_CastVote_Success_QueueFull_ShowsWarning()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();
        h.Queue.Setup(q => q.TryEnqueue(It.IsAny<int>())).Returns(false);

        var redirect = Assert.IsType<RedirectToActionResult>(await h.Controller.CastVote(Program1, CandA));

        Assert.Equal(nameof(VotingController.ThankYou), redirect.ActionName);
        Assert.Contains("blockchain block", h.Warning);
        Assert.Single(h.Db.Votes);
    }

    // P9: unique-constraint failure reported through the exception text -> "already cast"
    [Fact]
    public async Task P09_CastVote_UniqueViolationMessage_ShowsAlreadyVoted()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();
        h.Db.FailWith = new DbUpdateException("x", new Exception("duplicate key violates unique constraint"));

        var redirect = Assert.IsType<RedirectToActionResult>(await h.Controller.CastVote(Program1, CandA));

        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Contains("already cast", h.Error);
    }

    // P9b: the REAL unique index (VoterId + ProgramId) rejects a second vote row
    //      (voter.HasVoted is still false, e.g. two requests racing).
    [Fact]
    public async Task P09b_CastVote_RealUniqueIndex_RejectsSecondVote()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        var voter = h.AddVoter();
        h.AddVote(voter.Id);                       // a vote row already exists, HasVoted still false

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("already cast", h.Error);
        Assert.Single(h.Db.Votes);
    }

    // P10: other DbUpdateException -> generic error
    [Fact]
    public async Task P10_CastVote_GenericDbUpdateException_ShowsGenericError()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();
        h.Db.FailWith = new DbUpdateException("x", new Exception("connection lost"));

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("error occurred", h.Error);
    }

    // P11: any other exception -> generic error
    [Fact]
    public async Task P11_CastVote_UnexpectedException_ShowsGenericError()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();
        h.Db.FailWith = new InvalidOperationException("boom");

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("error occurred", h.Error);
    }

    // The second attempt of the same voter is refused and only one vote is stored
    [Fact]
    public async Task Extra_CastVote_SecondAttempt_IsRefused()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();

        await h.Controller.CastVote(Program1, CandA);
        await h.Controller.CastVote(Program1, VotingHarness.CandidateB);

        Assert.Contains("already cast", h.Error);
        Assert.Single(h.Db.Votes);
    }
}
