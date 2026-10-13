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
//  EQUIVALENCE CLASS TESTING (ECT)   -   module: ProgramOwnerController
//
//  ECT-01..08 and ECT-10..12, plus the EC19 image-URL class.
//  ECT-09 and ECT-13..20 failed before the fixes, so they are defect tests
//  (D01, D02, D03, D09, D10, D14) in ProgramOwnerDefectTests.cs.
//  The equivalence classes are in ProgramOwnerTestMatrix.md and
//  ProgramOwner_TestDesign.xlsx (sheet ECT).
// ─────────────────────────────────────────────────────────────────────────────
public sealed partial class ProgramOwnerBoundaryTests
{
    // ManageCandidates GET — current owner gets the candidate view (basis path P4)
    [Fact]
    public async Task ManageCandidates_CurrentOwner_ReturnsCandidateView()
    {
        await using var context = OwnerTestKit.CreateContext();
        var program = OwnerTestKit.Upcoming(1, "owner-1");
        program.Candidates.Add(OwnerTestKit.Candidate(1, 1));
        var service = new Mock<IVotingService>();
        service.Setup(s => s.GetProgramWithCandidatesAsync(1)).ReturnsAsync(program);
        var controller = OwnerTestKit.CreateController(context, service.Object);

        var result = Assert.IsType<ViewResult>(await controller.ManageCandidates(1));

        Assert.Equal("~/Views/VotingAdmin/ManageCandidates.cshtml", result.ViewName);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Candidate>>(result.Model));
    }

    // Basis path — AddCandidate: '/\host' and '//host' are treated by browsers as external hosts
    [Theory]
    [InlineData("/\\evil.example/a.png")]
    [InlineData("//evil.example/a.png")]
    public async Task AddCandidate_SlashBackslashOrDoubleSlashImageUrl_IsRejected(string imageUrl)
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);

        await controller.AddCandidate(1, "Valid Name", "C1", imageUrl, null);

        Assert.Equal("Please enter a valid image URL.", controller.TempData["Error"]);
        Assert.Empty(context.Candidates);
    }
}

public sealed partial class ProgramOwnerControllerTests
{
    [Fact]
    public async Task MyPrograms_WithoutUserId_ReturnsUnauthorized()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        SetUser(controller, null);

        Assert.IsType<UnauthorizedResult>(await controller.MyPrograms());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ManageCandidates_EnforcesOwnership(bool admin, bool allowed)
    {
        await using var context = CreateContext();
        var program = Program(1, "owner-1");
        var services = new Mock<IVotingService>();
        services.Setup(x => x.GetProgramWithCandidatesAsync(1)).ReturnsAsync(program);
        var controller = CreateController(context, services.Object);
        SetUser(controller, admin ? "other" : "other", admin);

        var result = await controller.ManageCandidates(1);

        Assert.Equal(allowed, result is ViewResult);
        if (!allowed) Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ManageVoters_OtherOwner_ReturnsForbid()
    {
        await using var context = CreateContext();
        context.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner", FullName = "Owner" });
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-2");

        Assert.IsType<ForbidResult>(await controller.ManageVoters(1));
    }

    [Fact]
    public async Task Results_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.GetProgramWithCandidatesAsync(1)).ReturnsAsync((VotingProgram?)null);

        Assert.IsType<NotFoundResult>(await CreateController(context, services.Object).Results(1));
    }

    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("http://example.com/image.png")]
    [InlineData("/images/candidate.png")]
    [InlineData("  /images/candidate.png  ")]
    public async Task AddCandidate_ValidImageUrl_Saves(string imageUrl)
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = await controller.AddCandidate(1, " Candidate ", " A ", imageUrl, " description ");

        AssertRedirectToManageCandidates(result, 1);
        var candidate = Assert.Single(context.Candidates);
        Assert.Equal("Candidate", candidate.Name);
        Assert.Equal("A", candidate.CandidateCode);
        Assert.Equal("description", candidate.Description);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/image.png")]
    [InlineData("example.com/image.png")]
    [InlineData("data:image/png;base64,abc")]
    public async Task AddCandidate_InvalidImageUrl_Redirects(string imageUrl)
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = await controller.AddCandidate(1, "Candidate", "A", imageUrl, null);

        AssertRedirectToManageCandidates(result, 1);
        Assert.Empty(context.Candidates);
    }

    [Fact]
    public async Task AddCandidate_DuplicateCode_RedirectsWithoutAdding()
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        context.Candidates.Add(new Candidate { Id = 1, ProgramId = 1, Name = "Existing", CandidateCode = "A" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = await controller.AddCandidate(1, "New", "A", null, null);

        AssertRedirectToManageCandidates(result, 1);
        Assert.Single(context.Candidates);
    }
}
