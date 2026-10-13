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
//  BOUNDARY VALUE ANALYSIS (BVA)   -   module: ProgramOwnerController
//
//  BVA-01..08, 10, 11, 16..20 and the 500-character description / image URL bound.
//  BVA-09 and BVA-12..15 failed before the fixes, so they are defect tests
//  (D07, D08) in ProgramOwnerDefectTests.cs.
//  The table of boundary values is in ProgramOwnerTestMatrix.md and
//  ProgramOwner_TestDesign.xlsx (sheet BVA).
// ─────────────────────────────────────────────────────────────────────────────
public sealed partial class ProgramOwnerBoundaryTests
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
    [InlineData(4, true)]
    [InlineData(199, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void ProgramName_LengthBoundary_MatchesModelRule(int length, bool valid)
    {
        var program = new VotingProgram { ProgramName = new string('a', length) };
        Assert.Equal(!valid, OwnerTestKit.HasValidationError(program, nameof(VotingProgram.ProgramName)));
    }

    // BVA-08  Description length (model rule <= 2000)
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(1999, true)]
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
    [InlineData(3, true)]
    [InlineData(149, true)]
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
    [InlineData(2, true)]
    [InlineData(49, true)]
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

    // BVA — description and image URL at their maximum (500) are accepted through the controller
    [Fact]
    public async Task AddCandidate_DescriptionAndImageUrlAtMaximumLength_AreSaved()
    {
        await using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Upcoming(1, "owner-1"));
        var controller = OwnerTestKit.CreateController(context);
        var imageUrl = "https://x.example/" + new string('p', 482); // 500 chars

        await controller.AddCandidate(1, "Valid Name", "C1", imageUrl, new string('d', 500));

        Assert.False(OwnerTestKit.Errored(controller));
        Assert.Single(context.Candidates);
    }
}
