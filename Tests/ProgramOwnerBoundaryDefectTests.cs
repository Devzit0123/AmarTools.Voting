using AmarTools.Voting.Controllers;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  Shared helpers (self-contained so this file does not depend on private
//  helpers inside ProgramOwnerControllerTests.cs)
// ─────────────────────────────────────────────────────────────────────────────
internal static class OwnerTestKit
{
    public static VotingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new VotingDbContext(options);
    }

    public static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var m = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        m.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        m.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Owner", UserName = "owner" });
        return m;
    }

    public static ProgramOwnerController CreateController(
        VotingDbContext context,
        IVotingService? votingService = null,
        IBlockchainService? blockchainService = null,
        string? userId = "owner-1",
        bool admin = false)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new ProgramOwnerController(
            context,
            votingService ?? Mock.Of<IVotingService>(),
            blockchainService ?? Mock.Of<IBlockchainService>(),
            MockUserManager().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
        SetUser(controller, userId, admin);
        return controller;
    }

    public static void SetUser(Controller controller, string? userId, bool admin = false)
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    public static VotingService CreateService(VotingDbContext context, string userId = "owner-1", bool admin = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "Owner")
        };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        var accessor = new HttpContextAccessor { HttpContext = http };

        return new VotingService(
            context, accessor, MockUserManager().Object, Mock.Of<ILogger<VotingService>>());
    }

    // Program states
    public static VotingProgram Upcoming(int id, string ownerId) => new()
    {
        Id = id,
        ProgramName = $"Program {id}",
        OwnerId = ownerId,
        StartTime = DateTime.UtcNow.AddHours(1),
        EndTime = DateTime.UtcNow.AddHours(2),
        IsPublished = false
    };

    public static VotingProgram Active(int id, string ownerId) => new()
    {
        Id = id,
        ProgramName = $"Program {id}",
        OwnerId = ownerId,
        StartTime = DateTime.UtcNow.AddHours(-1),
        EndTime = DateTime.UtcNow.AddHours(1),
        IsPublished = true
    };

    public static VotingProgram Ended(int id, string ownerId) => new()
    {
        Id = id,
        ProgramName = $"Program {id}",
        OwnerId = ownerId,
        StartTime = DateTime.UtcNow.AddHours(-2),
        EndTime = DateTime.UtcNow.AddHours(-1),
        IsPublished = true
    };

    public static Candidate Candidate(int id, int programId, string code = "C1") => new()
    {
        Id = id,
        ProgramId = programId,
        Name = $"Candidate {id}",
        CandidateCode = code
    };

    public static async Task SeedAsync(VotingDbContext context, params object[] entities)
    {
        context.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public static bool HasValidationError(object instance, string member)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results.Any(r => r.MemberNames.Contains(member));
    }

    public static bool Errored(ProgramOwnerController c) => c.TempData.ContainsKey("Error");
}

// ─────────────────────────────────────────────────────────────────────────────
//  PART 1 — Boundary / equivalence / basis-path tests that describe CURRENT
//  CORRECT behaviour. These are expected to PASS today.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class ProgramOwnerBoundaryTests
{
    // BVA-01..03  Program duration boundary (minimum 5 minutes = 300 s)
    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    [InlineData(301, true)]
    public async Task ProgramDuration_AtBoundary_IsEnforced(int seconds, bool accepted)
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = OwnerTestKit.CreateService(context);
        var start = DateTime.UtcNow.AddHours(1);
        var model = new VotingProgram
        {
            ProgramName = "Boundary Program",
            StartTime = start,
            EndTime = start.AddSeconds(seconds)
        };

        var (success, _) = await service.ValidateAndCreateProgramAsync(model);

        Assert.Equal(accepted, success);
    }

    // BVA-04..07  ProgramName length (model rule 3..200)
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void ProgramName_LengthBoundary_MatchesModelRule(int length, bool valid)
    {
        var program = new VotingProgram { ProgramName = new string('a', length) };
        Assert.Equal(!valid, OwnerTestKit.HasValidationError(program, nameof(VotingProgram.ProgramName)));
    }

    // BVA-08  Description length (model rule <= 2000)
    [Theory]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public void ProgramDescription_LengthBoundary_MatchesModelRule(int length, bool valid)
    {
        var program = new VotingProgram { ProgramName = "Valid name", Description = new string('d', length) };
        Assert.Equal(!valid, OwnerTestKit.HasValidationError(program, nameof(VotingProgram.Description)));
    }

    // BVA-09..12  Candidate name (model rule 2..150) — the MODEL is correct
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public void CandidateName_LengthBoundary_MatchesModelRule(int length, bool valid)
    {
        var candidate = new Candidate { Name = new string('n', length), CandidateCode = "C1", ProgramId = 1 };
        Assert.Equal(!valid, OwnerTestKit.HasValidationError(candidate, nameof(Candidate.Name)));
    }

    // BVA-13..14  Candidate code (model rule 1..50)
    [Theory]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void CandidateCode_LengthBoundary_MatchesModelRule(int length, bool valid)
    {
        var candidate = new Candidate { Name = "Valid Name", CandidateCode = new string('c', length), ProgramId = 1 };
        Assert.Equal(!valid, OwnerTestKit.HasValidationError(candidate, nameof(Candidate.CandidateCode)));
    }

    // BVA-15  Accepted boundary values reach the database through the controller
    [Theory]
    [InlineData(2, 1)]
    [InlineData(150, 50)]
    public async Task AddCandidate_NameAndCodeAtUpperAndLowerValidBounds_AreSaved(int nameLength, int codeLength)
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.AddCandidate(1, new string('N', nameLength), new string('C', codeLength), null, null);

        Assert.Single(context.Candidates);
        Assert.False(OwnerTestKit.Errored(controller));
    }

    // BVA-16..18  Identifier boundaries
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task EditGet_NonExistingIdBoundaries_ReturnNotFound(int id)
    {
        await using var context = OwnerTestKit.CreateContext();
        var controller = OwnerTestKit.CreateController(context);

        Assert.IsType<NotFoundResult>(await controller.Edit(id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task ManageCandidates_NonExistingIdBoundaries_ReturnNotFound(int programId)
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = new Mock<IVotingService>();
        service.Setup(s => s.GetProgramWithCandidatesAsync(It.IsAny<int>()))
            .ReturnsAsync((VotingProgram?)null);
        var controller = OwnerTestKit.CreateController(context, service.Object);

        Assert.IsType<NotFoundResult>(await controller.ManageCandidates(programId));
    }

    // BVA-19..20  Delete boundary: EndTime just passed vs. still running
    [Theory]
    [InlineData(-1, true)]
    [InlineData(60, false)]
    public async Task Delete_PublishedProgramAroundEndTime_RespectsBoundary(int secondsFromNow, bool deleted)
    {
        await using var context = OwnerTestKit.CreateContext();
        var program = OwnerTestKit.Active(1, "owner-1");
        program.EndTime = DateTime.UtcNow.AddSeconds(secondsFromNow);
        await OwnerTestKit.SeedAsync(context, program);
        var controller = OwnerTestKit.CreateController(context);

        await controller.Delete(1);

        Assert.Equal(deleted, !await context.VotingPrograms.AnyAsync(p => p.Id == 1));
    }

    // Basis-path additions not covered by the existing PO-xxx suite
    [Fact]
    public async Task AddCandidate_AdminOnAnotherOwnersProgram_SavesCandidate()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-2"));
        var controller = OwnerTestKit.CreateController(context, userId: "admin-1", admin: true);

        await controller.AddCandidate(1, "Alice Rahman", "A1", null, null);

        Assert.Single(context.Candidates);
    }

    [Fact]
    public async Task Results_AdminOnAnotherOwnersProgram_ReturnsView()
    {
        await using var context = OwnerTestKit.CreateContext();
        var program = OwnerTestKit.Ended(1, "owner-2");
        var service = new Mock<IVotingService>();
        service.Setup(s => s.GetProgramWithCandidatesAsync(1)).ReturnsAsync(program);
        service.Setup(s => s.GetResultsAsync(1)).ReturnsAsync(new List<CandidateResultViewModel>());
        var chain = new Mock<IBlockchainService>();
        chain.Setup(b => b.IsChainValidForProgramAsync(It.IsAny<VotingDbContext>(), 1)).ReturnsAsync(true);
        var controller = OwnerTestKit.CreateController(context, service.Object, chain.Object, "admin-1", admin: true);

        Assert.IsType<ViewResult>(await controller.Results(1));
    }

    [Fact]
    public async Task Delete_UnpublishedProgram_RemovesProgram()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.Delete(1);

        Assert.False(await context.VotingPrograms.AnyAsync());
    }

    // Create POST path: service fails WITHOUT an error message -> form returned, no model error added
    [Fact]
    public async Task CreatePost_ServiceFailureWithoutMessage_ReturnsFormWithoutModelError()
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = new Mock<IVotingService>();
        service.Setup(s => s.ValidateAndCreateProgramAsync(It.IsAny<VotingProgram>()))
            .ReturnsAsync((false, (string?)null));
        var controller = OwnerTestKit.CreateController(context, service.Object);

        var result = await controller.Create(new VotingProgram { ProgramName = "Valid name" });

        Assert.IsType<ViewResult>(result);
        Assert.Equal(0, controller.ModelState.ErrorCount);
    }

    // Edit POST path: service fails WITHOUT an error message
    [Fact]
    public async Task EditPost_ServiceFailureWithoutMessage_ReturnsFormWithoutModelError()
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = new Mock<IVotingService>();
        service.Setup(s => s.ValidateAndUpdateProgramAsync(1, It.IsAny<VotingProgram>()))
            .ReturnsAsync((false, (string?)null));
        var controller = OwnerTestKit.CreateController(context, service.Object);

        var result = await controller.Edit(1, new VotingProgram { Id = 1, ProgramName = "Valid name" });

        Assert.IsType<ViewResult>(result);
        Assert.Equal(0, controller.ModelState.ErrorCount);
    }

    // Edit POST path: success with IsPublished = true -> published link stored, redirect to MyPrograms
    [Fact]
    public async Task EditPost_PublishedSuccess_StoresPublishedLinkAndRedirects()
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = new Mock<IVotingService>();
        service.Setup(s => s.ValidateAndUpdateProgramAsync(1, It.IsAny<VotingProgram>()))
            .ReturnsAsync((true, (string?)null));
        var controller = OwnerTestKit.CreateController(context, service.Object);
        controller.Url = Mock.Of<IUrlHelper>();

        var result = await controller.Edit(1, new VotingProgram { Id = 1, ProgramName = "Valid name", IsPublished = true });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProgramOwnerController.MyPrograms), redirect.ActionName);
        Assert.True(controller.TempData.ContainsKey("PublishedLink"));
    }

    // RegisterVoter POST path: admin acting on another owner's program reaches the service
    [Fact]
    public async Task RegisterVoterPost_AdminOnAnotherOwnersProgram_CallsService()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-2"));
        var service = new Mock<IVotingService>();
        service.Setup(s => s.RegisterVoterByEmailAsync(1, "Nadia Islam", "nadia@example.com", null, "owner"))
            .ReturnsAsync((true, (string?)null));
        var controller = OwnerTestKit.CreateController(context, service.Object, userId: "admin-1", admin: true);

        await controller.RegisterVoter(1, "Nadia Islam", "nadia@example.com", null);

        service.Verify(s => s.RegisterVoterByEmailAsync(1, "Nadia Islam", "nadia@example.com", null, "owner"), Times.Once);
    }
}

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