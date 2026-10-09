using AmarTools.Voting.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  CastVote paths that depend on PostgreSQL error codes.
//
//  If your build reports an error on `new PostgresException(...)` (the constructor differs between
//  Npgsql versions) simply delete THIS file; every other test is independent of it.
// ─────────────────────────────────────────────────────────────────────────────
[Trait("Technique", "BasisPath")]
public sealed class VotingControllerNpgsqlTests
{
    private const int Program1 = VotingHarness.ProgramId;
    private const int CandA = VotingHarness.CandidateA;

    // P12  PostgreSQL SQLSTATE 23505 (unique_violation) -> "already cast".
    //      The message text deliberately does NOT contain "unique constraint", so only the SqlState
    //      check of the controller can produce the expected result.
    [Fact]
    public async Task P12_CastVote_PostgresUniqueViolation_ShowsAlreadyVoted()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();
        h.Db.FailWith = new DbUpdateException(
            "save failed",
            new PostgresException("duplicate row", "ERROR", "ERROR", "23505"));

        var redirect = Assert.IsType<RedirectToActionResult>(await h.Controller.CastVote(Program1, CandA));

        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Contains("already cast", h.Error);
    }

    // P13  SQLSTATE 40001 (serialization_failure) is what a SERIALIZABLE transaction raises under
    //      contention. The controller shows the generic error instead of retrying (see defect DV-10).
    [Fact]
    public async Task P13_CastVote_PostgresSerializationFailure_ShowsGenericError()
    {
        using var h = new VotingHarness();
        h.AddProgram();
        h.AddVoter();
        h.Db.FailWith = new DbUpdateException(
            "save failed",
            new PostgresException("could not serialize access", "ERROR", "ERROR", "40001"));

        await h.Controller.CastVote(Program1, CandA);

        Assert.Contains("error occurred", h.Error);
    }
}
