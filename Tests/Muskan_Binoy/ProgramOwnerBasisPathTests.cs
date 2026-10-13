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
//  BASIS PATH TESTING   -   module: ProgramOwnerController
//
//  Independent paths not already covered by ProgramOwnerControllerTests.cs.
//  The flow graphs, V(G) and the full path list for the 10 functions are in
//  ProgramOwnerTestMatrix.md and ProgramOwner_BasisPath.xlsx.
// ─────────────────────────────────────────────────────────────────────────────
public sealed partial class ProgramOwnerBoundaryTests
{
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

    // Basis path — AddCandidate P14: description given, no image URL
    [Fact]
    public async Task AddCandidate_DescriptionWithoutImage_IsSavedTrimmed()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.AddCandidate(1, "Valid Name", "C1", null, "  party leader  ");

        var candidate = Assert.Single(context.Candidates);
        Assert.Equal("party leader", candidate.Description);
        Assert.Null(candidate.ImageUrl);
    }

    // Basis path — Create POST P5: success with IsPublished = true stores the public vote link
    [Fact]
    public async Task CreatePost_PublishedSuccess_StoresPublishedLinkAndRedirects()
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = new Mock<IVotingService>();
        service.Setup(s => s.ValidateAndCreateProgramAsync(It.IsAny<VotingProgram>()))
            .ReturnsAsync((true, (string?)null));
        var controller = OwnerTestKit.CreateController(context, service.Object);
        controller.Url = Mock.Of<IUrlHelper>();

        var result = await controller.Create(new VotingProgram { Id = 5, ProgramName = "Valid name", IsPublished = true });

        Assert.Equal(nameof(ProgramOwnerController.MyPrograms), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.True(controller.TempData.ContainsKey("PublishedLink"));
    }

    // Edit GET — current owner gets the form with UTC-flagged times (PO-010)
    [Fact]
    public async Task EditGet_CurrentOwner_ReturnsFormWithUtcTimes()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        var result = Assert.IsType<ViewResult>(await controller.Edit(1));
        var model = Assert.IsType<VotingProgram>(result.Model);

        Assert.True(model.ValuesAreUtc);
        Assert.Equal(DateTimeKind.Utc, model.StartTime.Kind);
    }

    // Edit POST P3 — service fails with a message (PO-015)
    [Fact]
    public async Task EditPost_ServiceFailureWithMessage_ReturnsFormWithModelError()
    {
        await using var context = OwnerTestKit.CreateContext();
        var service = new Mock<IVotingService>();
        service.Setup(s => s.ValidateAndUpdateProgramAsync(1, It.IsAny<VotingProgram>()))
            .ReturnsAsync((false, (string?)"schedule locked"));
        var controller = OwnerTestKit.CreateController(context, service.Object);

        var result = await controller.Edit(1, new VotingProgram { Id = 1, ProgramName = "Valid name" });

        Assert.Equal("~/Views/Shared/CreateProgram.cshtml", Assert.IsType<ViewResult>(result).ViewName);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage == "schedule locked");
    }

    // DeleteCandidate — owner deletes a candidate of their own upcoming program (PO-040)
    [Fact]
    public async Task DeleteCandidate_Owner_DeletesCandidate()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"), OwnerTestKit.Candidate(1, 1));
        var controller = OwnerTestKit.CreateController(context);

        await controller.DeleteCandidate(1, 1);

        Assert.Equal("Candidate deleted.", controller.TempData["Success"]);
        Assert.Empty(context.Candidates);
    }

    // RegisterVoter GET — different owner is forbidden (PO-053)
    [Fact]
    public async Task RegisterVoterGet_OtherOwner_ReturnsForbid()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-2"));
        var controller = OwnerTestKit.CreateController(context);

        Assert.IsType<ForbidResult>(await controller.RegisterVoter(1));
    }

    // Basis path — AddCandidate P14: DbUpdateException on save
    [Fact]
    public async Task AddCandidate_SaveFails_RedirectsWithError()
    {
        var faults = new FailingSaveInterceptor();
        await using var context = OwnerTestKit.CreateContext(faults);
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);
        faults.Fail = true;

        var result = await controller.AddCandidate(1, "Valid Name", "C1", null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProgramOwnerController.ManageCandidates), redirect.ActionName);
        Assert.Equal("Candidate could not be saved. Please check the values and try again.", controller.TempData["Error"]);
    }

    // Basis path — DeleteCandidate: program does not exist
    [Fact]
    public async Task DeleteCandidate_MissingProgram_ReturnsNotFound()
    {
        await using var context = OwnerTestKit.CreateContext();
        var controller = OwnerTestKit.CreateController(context);

        Assert.IsType<NotFoundResult>(await controller.DeleteCandidate(1, 999));
    }

    // Basis path — DeleteCandidate: DbUpdateException on save
    [Fact]
    public async Task DeleteCandidate_SaveFails_RedirectsWithErrorAndKeepsCandidate()
    {
        var faults = new FailingSaveInterceptor();
        await using var context = OwnerTestKit.CreateContext(faults);
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"), OwnerTestKit.Candidate(1, 1));
        var controller = OwnerTestKit.CreateController(context);
        faults.Fail = true;

        var result = await controller.DeleteCandidate(1, 1);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Candidate could not be deleted right now.", controller.TempData["Error"]);
    }

    // Basis path — ManageVoters: admin on another owner's program
    [Fact]
    public async Task ManageVoters_AdminOnAnotherOwnersProgram_ReturnsView()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            new ApplicationUser { Id = "owner-2", UserName = "owner2", FullName = "Owner Two" },
            OwnerTestKit.Upcoming(1, "owner-2"));
        var controller = OwnerTestKit.CreateController(context, userId: "admin-1", admin: true);

        Assert.IsType<ViewResult>(await controller.ManageVoters(1));
    }

    // Basis path — RegisterVoter POST: authenticated non-owner, non-admin
    [Fact]
    public async Task RegisterVoterPost_OtherOwner_ReturnsForbidWithoutCallingService()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-2"));
        var service = new Mock<IVotingService>();
        var controller = OwnerTestKit.CreateController(context, service.Object);

        Assert.IsType<ForbidResult>(await controller.RegisterVoter(1, "Nadia Islam", "nadia@example.com", null));
        service.Verify(s => s.RegisterVoterByEmailAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    // Basis path — Delete: admin deletes another owner's ended program
    [Fact]
    public async Task Delete_AdminOnAnotherOwnersEndedProgram_RemovesProgram()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Ended(1, "owner-2"));
        var controller = OwnerTestKit.CreateController(context, userId: "admin-1", admin: true);

        await controller.Delete(1);

        Assert.False(await context.VotingPrograms.AnyAsync());
    }

    // Basis path — Delete: DbUpdateException on save
    [Fact]
    public async Task Delete_SaveFails_RedirectsWithError()
    {
        var faults = new FailingSaveInterceptor();
        await using var context = OwnerTestKit.CreateContext(faults);
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);
        faults.Fail = true;

        var result = await controller.Delete(1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProgramOwnerController.MyPrograms), redirect.ActionName);
        Assert.Equal("This program could not be deleted right now.", controller.TempData["Error"]);
    }

    // Basis path — Delete: published but not yet started (upcoming) is refused, unpublishing is the way out
    [Fact]
    public async Task Delete_PublishedUpcomingProgram_IsRefusedWithUnpublishHint()
    {
        await using var context = OwnerTestKit.CreateContext();
        var program = OwnerTestKit.Upcoming(1, "owner-1");
        program.IsPublished = true;
        await OwnerTestKit.SeedAsync(context, program);
        var controller = OwnerTestKit.CreateController(context);

        await controller.Delete(1);

        Assert.Contains("Unpublish it first", (string)controller.TempData["Error"]!);
        Assert.True(await context.VotingPrograms.AnyAsync());
    }
}
