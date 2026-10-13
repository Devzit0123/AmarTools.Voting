using AmarTools.Voting.Controllers;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  PART 2 — DEFECT-CONFIRMING TESTS
//
//  Every test below asserts the CORRECT behaviour. Based on reading the code,
//  each one is expected to FAIL today and PASS once the defect is fixed.
//
//    Show only the defects :  dotnet test --filter "Category=Defect"
//    Keep CI green         :  dotnet test --filter "Category!=Defect"
//
//  Record the actual result of each in the Defects sheet before you report it.
// ─────────────────────────────────────────────────────────────────────────────
[Trait("Category", "Defect")]
public sealed class ProgramOwnerDefectTests
{
    // D01 — Edit can reschedule or reopen a program after voting has started/ended
    [Fact]
    public async Task D01_Edit_StartedProgram_CannotBeRescheduledIntoTheFuture()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Active(1, "owner-1"));
        var service = OwnerTestKit.CreateService(context);
        var model = new VotingProgram
        {
            Id = 1,
            ProgramName = "Program 1",
            IsPublished = true,
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4)
        };

        var (success, _) = await service.ValidateAndUpdateProgramAsync(1, model);

        Assert.False(success);
    }

    [Fact]
    public async Task D01_Edit_EndedProgram_CannotBeReopened()
    {
        await using var context = OwnerTestKit.CreateContext();
        var ended = OwnerTestKit.Ended(1, "owner-1");
        await OwnerTestKit.SeedAsync(context, ended);
        var service = OwnerTestKit.CreateService(context);
        var model = new VotingProgram
        {
            Id = 1,
            ProgramName = "Program 1",
            IsPublished = true,
            StartTime = ended.StartTime,
            EndTime = DateTime.UtcNow.AddHours(3)
        };

        var (success, _) = await service.ValidateAndUpdateProgramAsync(1, model);

        Assert.False(success);
    }

    // D02 — Candidates can be added/deleted while the election is running or after it ended
    [Fact]
    public async Task D02_AddCandidate_WhileProgramIsActive_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Active(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.AddCandidate(1, "Late Entry", "L1", null, null);

        Assert.True(OwnerTestKit.Errored(controller));
        Assert.Empty(context.Candidates);
    }

    [Fact]
    public async Task D02_AddCandidate_AfterProgramEnded_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Ended(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.AddCandidate(1, "Late Entry", "L1", null, null);

        Assert.True(OwnerTestKit.Errored(controller));
        Assert.Empty(context.Candidates);
    }

    [Fact]
    public async Task D02_DeleteCandidate_WhileProgramIsActive_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            OwnerTestKit.Active(1, "owner-1"), OwnerTestKit.Candidate(1, 1));
        var controller = OwnerTestKit.CreateController(context);

        await controller.DeleteCandidate(1, 1);

        Assert.True(OwnerTestKit.Errored(controller));
        Assert.Single(context.Candidates);
    }

    // D03 — Deleting a candidate that already has votes (DB cascade removes those votes)
    [Fact]
    public async Task D03_DeleteCandidate_WithRecordedVotes_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            OwnerTestKit.Upcoming(1, "owner-1"),
            OwnerTestKit.Candidate(1, 1),
            new Voter { Id = 1, Name = "Voter One", ProgramId = 1, HasVoted = true },
            new Vote { Id = 1, ProgramId = 1, CandidateId = 1, VoterId = 1 });
        var controller = OwnerTestKit.CreateController(context);

        await controller.DeleteCandidate(1, 1);

        Assert.Single(context.Candidates);
    }

    // D04 — Removing a voter who already voted also removes the vote (FK cascade)
    [Fact]
    public async Task D04_RemoveVoter_VoterWhoAlreadyVoted_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            OwnerTestKit.Active(1, "owner-1"),
            OwnerTestKit.Candidate(1, 1),
            new Voter { Id = 1, Name = "Voter One", ProgramId = 1, HasVoted = true },
            new Vote { Id = 1, ProgramId = 1, CandidateId = 1, VoterId = 1 });
        var service = OwnerTestKit.CreateService(context);

        var (success, _) = await service.RemoveVoterAsync(1, 1);

        Assert.False(success);
        Assert.Single(context.Voters);
    }

    // D05 — Program with recorded votes can be hard-deleted right after it ends
    [Fact]
    public async Task D05_Delete_EndedProgramWithVotes_IsNotHardDeleted()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            OwnerTestKit.Ended(1, "owner-1"),
            OwnerTestKit.Candidate(1, 1),
            new Voter { Id = 1, Name = "Voter One", ProgramId = 1, HasVoted = true },
            new Vote { Id = 1, ProgramId = 1, CandidateId = 1, VoterId = 1 });
        var controller = OwnerTestKit.CreateController(context);

        await controller.Delete(1);

        Assert.True(await context.VotingPrograms.AnyAsync(p => p.Id == 1));
    }

    // D06 — After a duration-validation failure the model claims "UTC" but still holds local times
    [Fact]
    public async Task D06_DurationFailure_DoesNotLeaveLocalTimesFlaggedAsUtc()
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = OwnerTestKit.CreateService(context);
        var start = DateTime.SpecifyKind(DateTime.Now.AddHours(1), DateTimeKind.Unspecified);
        var model = new VotingProgram
        {
            ProgramName = "Too Short",
            StartTime = start,
            EndTime = start.AddMinutes(1),
            StartTimeOffsetMinutes = 360,
            EndTimeOffsetMinutes = 360
        };

        var (success, _) = await service.ValidateAndCreateProgramAsync(model);

        Assert.False(success);
        Assert.False(model.ValuesAreUtc && model.StartTime.Kind == DateTimeKind.Unspecified,
            "ValuesAreUtc is true but StartTime is still the browser-local value.");
    }

    // D07 / D08 — Server does not enforce the same limits as the model
    [Fact]
    public async Task D07_AddCandidate_SingleCharacterName_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.AddCandidate(1, "A", "C1", null, null);

        Assert.True(OwnerTestKit.Errored(controller));
        Assert.Empty(context.Candidates);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("code")]
    [InlineData("description")]
    [InlineData("imageUrl")]
    public async Task D08_AddCandidate_FieldOverMaxLength_IsRejectedWithValidationMessage(string field)
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        var name = field == "name" ? new string('N', 151) : "Valid Name";
        var code = field == "code" ? new string('C', 51) : "C1";
        var description = field == "description" ? new string('d', 501) : null;
        var imageUrl = field == "imageUrl" ? "https://x.example/" + new string('p', 483) : null; // 501 chars

        await controller.AddCandidate(1, name, code, imageUrl, description);

        Assert.True(OwnerTestKit.Errored(controller));
        Assert.Empty(context.Candidates);
    }

    // D09 — Protocol-relative URL slips through the "starts with /" rule
    [Fact]
    public async Task D09_AddCandidate_ProtocolRelativeImageUrl_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.AddCandidate(1, "Valid Name", "C1", "//evil.example/photo.png", null);

        Assert.True(OwnerTestKit.Errored(controller));
        Assert.Empty(context.Candidates);
    }

    // D10 — Candidate-code uniqueness is case-sensitive
    [Fact]
    public async Task D10_AddCandidate_CodeThatDiffersOnlyByCase_IsRejectedAsDuplicate()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            OwnerTestKit.Upcoming(1, "owner-1"), OwnerTestKit.Candidate(1, 1, "A"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.AddCandidate(1, "Second Person", "a", null, null);

        Assert.True(OwnerTestKit.Errored(controller));
        Assert.Single(context.Candidates);
    }

    // D11 — Inconsistent NotFound / Forbid reveals which candidate IDs exist
    [Fact]
    public async Task D11_DeleteCandidate_ExistingAndMissingCandidateOfOtherProgram_BehaveTheSame()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            OwnerTestKit.Upcoming(1, "owner-1"),
            OwnerTestKit.Upcoming(2, "owner-2"),
            OwnerTestKit.Candidate(1, 2));
        var controller = OwnerTestKit.CreateController(context);

        var existingElsewhere = await controller.DeleteCandidate(1, 1);
        var missing = await controller.DeleteCandidate(999, 1);

        Assert.Equal(existingElsewhere.GetType(), missing.GetType());
    }

    [Fact]
    public async Task D11_RemoveVoter_MissingProgram_ReturnsNotFoundLikeTheOtherActions()
    {
        await using var context = OwnerTestKit.CreateContext();
        var controller = OwnerTestKit.CreateController(context);

        Assert.IsType<NotFoundResult>(await controller.RemoveVoter(1, 999));
    }

    [Fact]
    public async Task D11_RegisterVoterPost_MissingProgram_ReturnsNotFoundLikeTheOtherActions()
    {
        await using var context = OwnerTestKit.CreateContext();
        var controller = OwnerTestKit.CreateController(context);

        Assert.IsType<NotFoundResult>(await controller.RegisterVoter(999, "Valid Name", "v@example.com", null));
    }

    // D12 — list pages load counts from the database instead of every vote, voter and candidate row
    private static async Task<VotingDbContext> SeedProgramWithActivityAsync(string ownerId = "owner-1")
    {
        var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            OwnerTestKit.Ended(1, ownerId),
            OwnerTestKit.Candidate(1, 1, "A"), OwnerTestKit.Candidate(2, 1, "B"),
            new Voter { Id = 1, Name = "Voter One", ProgramId = 1, HasVoted = true },
            new Voter { Id = 2, Name = "Voter Two", ProgramId = 1, HasVoted = true },
            new Voter { Id = 3, Name = "Voter Three", ProgramId = 1 },
            new Vote { Id = 1, ProgramId = 1, CandidateId = 1, VoterId = 1 },
            new Vote { Id = 2, ProgramId = 1, CandidateId = 2, VoterId = 2 });
        context.ChangeTracker.Clear();
        return context;
    }

    [Fact]
    public async Task D12_MyPrograms_ShowsCountsWithoutLoadingVotesOrVoters()
    {
        await using var context = await SeedProgramWithActivityAsync();
        var controller = OwnerTestKit.CreateController(context);

        var result = Assert.IsType<ViewResult>(await controller.MyPrograms());
        var program = Assert.Single(Assert.IsAssignableFrom<IEnumerable<VotingProgram>>(result.Model));

        Assert.Equal((2, 2, 3), (program.CandidateCount, program.TotalVotes, program.VoterCount));
        Assert.Empty(program.Votes);
        Assert.Empty(program.Voters);
        Assert.Empty(program.Candidates);
    }

    [Fact]
    public async Task D12_AdminDashboard_ShowsCountsWithoutLoadingVotesOrVoters()
    {
        await using var context = await SeedProgramWithActivityAsync();
        var service = OwnerTestKit.CreateService(context, admin: true);

        var program = Assert.Single(await service.GetAllProgramsForDashboardAsync());

        Assert.Equal((2, 2, 3), (program.CandidateCount, program.TotalVotes, program.VoterCount));
        Assert.Empty(program.Votes);
        Assert.Empty(program.Voters);
    }

    // D34 — the ManageCandidates header always showed "0 vote(s)": the program was loaded without its votes
    [Fact]
    public async Task D34_ManageCandidates_HeaderVoteCountIncludesRecordedVotes()
    {
        await using var context = await SeedProgramWithActivityAsync();
        var program = OwnerTestKit.Ended(1, "owner-1");
        var service = new Mock<IVotingService>();
        service.Setup(s => s.GetProgramWithCandidatesAsync(1)).ReturnsAsync(program);
        var controller = OwnerTestKit.CreateController(context, service.Object);

        await controller.ManageCandidates(1);

        Assert.Equal(2, ((VotingProgram)controller.ViewBag.Program).TotalVotes);
    }

    // D33 — Delete tells the owner to "unpublish first", but an active program cannot be unpublished (D01 fix)
    [Fact]
    public async Task D33_Delete_ActiveProgram_DoesNotSuggestAnImpossibleUnpublish()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Active(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.Delete(1);

        var error = Assert.IsType<string>(controller.TempData["Error"]);
        Assert.DoesNotContain("Unpublish", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(await context.VotingPrograms.AnyAsync());
    }

    // D01 regression guard — the fix must still allow renaming an active program
    [Fact]
    public async Task D01_Edit_ActiveProgram_NameOnlyChange_IsAccepted()
    {
        await using var context = OwnerTestKit.CreateContext();
        var active = OwnerTestKit.Active(1, "owner-1");
        await OwnerTestKit.SeedAsync(context, active);
        var service = OwnerTestKit.CreateService(context);
        var model = new VotingProgram
        {
            Id = 1,
            ProgramName = "Renamed Program",
            IsPublished = true,
            StartTime = active.StartTime,
            EndTime = active.EndTime
        };

        var (success, _) = await service.ValidateAndUpdateProgramAsync(1, model);

        Assert.True(success);
        Assert.Equal("Renamed Program", (await context.VotingPrograms.SingleAsync()).ProgramName);
    }

    // D13 — Create does not trim Description (Edit does)
    [Fact]
    public async Task D13_Create_TrimsDescriptionLikeEditDoes()
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = OwnerTestKit.CreateService(context);
        var start = DateTime.UtcNow.AddHours(1);
        var model = new VotingProgram
        {
            ProgramName = "Trim Test",
            Description = "  hello  ",
            StartTime = start,
            EndTime = start.AddHours(1)
        };

        var (success, _) = await service.ValidateAndCreateProgramAsync(model);

        Assert.True(success);
        Assert.Equal("hello", (await context.VotingPrograms.SingleAsync()).Description);
    }

    // D14 — Voter registration has no email-format check and ignores the election window
    [Fact]
    public async Task D14_RegisterVoter_InvalidEmailFormat_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var service = OwnerTestKit.CreateService(context);

        var (success, _) = await service.RegisterVoterByEmailAsync(1, "Valid Name", "not-an-email", null, "owner");

        Assert.False(success);
    }

    // D29 — voter name length (model rule 2..150) is enforced on registration
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(12, true)]
    [InlineData(149, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public async Task D29_RegisterVoter_NameLengthBoundary_IsEnforced(int length, bool accepted)
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var service = OwnerTestKit.CreateService(context);

        var (success, _) = await service.RegisterVoterByEmailAsync(1, new string('v', length), "voter@example.com", null, "owner");

        Assert.Equal(accepted, success);
    }

    [Fact]
    public async Task D14_RegisterVoter_AfterElectionEnded_IsRejected()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Ended(1, "owner-1"));
        var service = OwnerTestKit.CreateService(context);

        var (success, _) = await service.RegisterVoterByEmailAsync(1, "Valid Name", "late@example.com", null, "owner");

        Assert.False(success);
    }
}
