using System.Security.Claims;
using AmarTools.Voting.Controllers;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using AmarTools.Voting.Services.Background;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AmarTools.Voting.Tests;

public class VotePathTests
{
    [Fact]
    public async Task JoinAtProgramStart_DoesNotRegisterAnUnregisteredVoter()
    {
        await using var context = CreateContext();
        var user = new ApplicationUser
        {
            Id = "voter-1",
            UserName = "student@example.com",
            Email = "student@example.com",
            FullName = "Student"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var program = CreateProgram(DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddHours(1));
        var controller = CreateVotingController(context, program, "voter-1");

        var result = await controller.Join(program.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Equal("Only the program owner can register voters for this program.", controller.TempData["Error"]);
        Assert.Empty(await context.Voters.ToListAsync());
    }

    [Fact]
    public async Task CastVoteAfterProgramEnd_RejectsTheVote()
    {
        await using var context = CreateContext();
        var program = CreateProgram(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMilliseconds(-1));
        var candidate = new Candidate { Id = 10, ProgramId = program.Id, Name = "Candidate", CandidateCode = "A" };
        context.Voters.Add(new Voter { Id = 20, ProgramId = program.Id, UserId = "voter-1", Name = "Student" });
        await context.SaveChangesAsync();

        program.Candidates.Add(candidate);
        var controller = CreateVotingController(context, program, "voter-1");

        var result = await controller.CastVote(program.Id, candidate.Id);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Empty(await context.Votes.ToListAsync());
    }

    [Fact]
    public async Task FailedCreateSubmission_ResetsValuesAreUtcForTheForm()
    {
        await using var context = CreateContext();
        var controller = CreateProgramOwnerController(context);
        controller.ModelState.AddModelError(nameof(VotingProgram.ProgramName), "required");

        var model = CreateProgram(DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        model.ValuesAreUtc = true;

        var result = await controller.Create(model);

        var view = Assert.IsType<ViewResult>(result);
        var returnedModel = Assert.IsType<VotingProgram>(view.Model);
        Assert.False(returnedModel.ValuesAreUtc);
    }

    private static VotingProgram CreateProgram(DateTime start, DateTime end) => new()
    {
        Id = 1,
        ProgramName = "Campus Election",
        StartTime = start,
        EndTime = end,
        IsPublished = true,
        OwnerId = "owner-1"
    };

    private static VotingController CreateVotingController(
        VotingDbContext context, VotingProgram program, string userId)
    {
        var service = new Mock<IVotingService>();
        service.Setup(x => x.GetProgramWithCandidatesAsync(program.Id)).ReturnsAsync(program);

        var controller = new VotingController(
            context,
            service.Object,
            Mock.Of<IBlockchainService>(),
            Mock.Of<IVoteBlockQueue>());
        SetUser(controller, userId);
        return controller;
    }

    private static ProgramOwnerController CreateProgramOwnerController(VotingDbContext context)
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!).Object;

        var controller = new ProgramOwnerController(
            context,
            Mock.Of<IVotingService>(),
            Mock.Of<IBlockchainService>(),
            userManager);
        SetUser(controller, "owner-1");
        return controller;
    }

    private static void SetUser(Controller controller, string userId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    private static VotingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new VotingDbContext(options);
    }
}
