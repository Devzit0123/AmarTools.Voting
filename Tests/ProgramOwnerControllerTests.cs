using System.Security.Claims;
using AmarTools.Voting.Controllers;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AmarTools.Voting.Tests;

public sealed class ProgramOwnerControllerTests
{
    [Fact]
    public async Task MyPrograms_WithoutUserId_ReturnsUnauthorized()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        SetUser(controller, null);

        Assert.IsType<UnauthorizedResult>(await controller.MyPrograms());
    }

    [Fact]
    public async Task MyPrograms_ReturnsOnlyCurrentOwnersPrograms()
    {
        await using var context = CreateContext();
        context.VotingPrograms.AddRange(
            Program(1, "owner-1"),
            Program(2, "owner-2"));
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = Assert.IsType<ViewResult>(await controller.MyPrograms());
        var programs = Assert.IsAssignableFrom<IEnumerable<VotingProgram>>(result.Model);

        Assert.Single(programs);
        Assert.Equal(1, programs.Single().Id);
    }

    [Fact]
    public void CreateGet_ReturnsUtcModel()
    {
        using var context = CreateContext();
        var result = Assert.IsType<ViewResult>(CreateController(context).Create());
        var model = Assert.IsType<VotingProgram>(result.Model);

        Assert.Equal("~/Views/Shared/CreateProgram.cshtml", result.ViewName);
        Assert.True(model.ValuesAreUtc);
        Assert.True(model.EndTime > model.StartTime);
    }

    [Fact]
    public async Task CreatePost_InvalidModel_ReturnsLocalInputMode()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        controller.ModelState.AddModelError(nameof(VotingProgram.ProgramName), "required");
        var model = Program(0, "invalid");
        model.ValuesAreUtc = true;

        var result = Assert.IsType<ViewResult>(await controller.Create(model));

        Assert.False(Assert.IsType<VotingProgram>(result.Model).ValuesAreUtc);
    }

    [Fact]
    public async Task CreatePost_ServiceFailure_ReturnsValidationError()
    {
        await using var context = CreateContext();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.ValidateAndCreateProgramAsync(It.IsAny<VotingProgram>()))
            .ReturnsAsync((false, (string?)"invalid time range"));
        var controller = CreateController(context, services.Object);

        var result = Assert.IsType<ViewResult>(await controller.Create(Program(0, "new")));

        Assert.Contains(controller.ModelState.Values.SelectMany(x => x.Errors), e => e.ErrorMessage == "invalid time range");
        Assert.Equal("~/Views/Shared/CreateProgram.cshtml", result.ViewName);
    }

    [Fact]
    public async Task CreatePost_PublishedProgram_RedirectsToMyPrograms()
    {
        await using var context = CreateContext();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.ValidateAndCreateProgramAsync(It.IsAny<VotingProgram>()))
            .ReturnsAsync((true, (string?)null));
        var controller = CreateController(context, services.Object);
        var model = Program(10, "new");

        var result = Assert.IsType<RedirectToActionResult>(await controller.Create(model));

        Assert.Equal(nameof(ProgramOwnerController.MyPrograms), result.ActionName);
        Assert.Equal("Voting program created successfully.", controller.TempData["Success"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task EditGet_EnforcesOwnershipOrAdmin(bool admin, bool allowed)
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, admin ? "other" : "other", admin);

        var result = await controller.Edit(1);

        if (allowed)
            Assert.IsType<ViewResult>(result);
        else
            Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task EditGet_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();

        Assert.IsType<NotFoundResult>(await CreateController(context).Edit(404));
    }

    [Fact]
    public async Task EditPost_IdMismatch_ReturnsNotFound()
    {
        await using var context = CreateContext();

        Assert.IsType<NotFoundResult>(await CreateController(context).Edit(1, Program(2, "owner-1")));
    }

    [Fact]
    public async Task EditPost_InvalidModel_ReturnsForm()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        controller.ModelState.AddModelError("ProgramName", "required");

        var result = Assert.IsType<ViewResult>(await controller.Edit(1, Program(1, "invalid")));

        Assert.Equal("~/Views/Shared/CreateProgram.cshtml", result.ViewName);
    }

    [Fact]
    public async Task EditPost_ServiceSuccess_Redirects()
    {
        await using var context = CreateContext();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.ValidateAndUpdateProgramAsync(1, It.IsAny<VotingProgram>()))
            .ReturnsAsync((true, (string?)null));
        var controller = CreateController(context, services.Object);

        var result = Assert.IsType<RedirectToActionResult>(await controller.Edit(1, Program(1, "updated")));

        Assert.Equal(nameof(ProgramOwnerController.MyPrograms), result.ActionName);
    }

    [Fact]
    public async Task ManageCandidates_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.GetProgramWithCandidatesAsync(1)).ReturnsAsync((VotingProgram?)null);

        Assert.IsType<NotFoundResult>(await CreateController(context, services.Object).ManageCandidates(1));
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

    [Theory]
    [InlineData(null, "A")]
    [InlineData("", "A")]
    [InlineData(" ", "A")]
    [InlineData("Candidate", null)]
    [InlineData("Candidate", "")]
    [InlineData("Candidate", " ")]
    public async Task AddCandidate_RequiredFieldsRedirect(string? name, string? code)
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = await controller.AddCandidate(1, name!, code!, null, null);

        AssertRedirectToManageCandidates(result, 1);
        Assert.Empty(context.Candidates);
    }

    [Fact]
    public async Task AddCandidate_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        Assert.IsType<NotFoundResult>(await controller.AddCandidate(1, "Candidate", "A", null, null));
    }

    [Fact]
    public async Task AddCandidate_OtherOwner_ReturnsForbid()
    {
        await using var context = CreateContext();
        context.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner", FullName = "Owner" });
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-2");

        Assert.IsType<ForbidResult>(await controller.AddCandidate(1, "Candidate", "A", null, null));
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

    [Fact]
    public async Task DeleteCandidate_MissingCandidate_ReturnsNotFound()
    {
        await using var context = CreateContext();
        Assert.IsType<NotFoundResult>(await CreateController(context).DeleteCandidate(1, 1));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DeleteCandidate_EnforcesOwnership(bool admin, bool allowed)
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        context.Candidates.Add(new Candidate { Id = 2, ProgramId = 1, Name = "Candidate", CandidateCode = "A" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, admin ? "other" : "other", admin);

        var result = await controller.DeleteCandidate(2, 1);

        if (allowed)
        {
            AssertRedirectToManageCandidates(result, 1);
            Assert.Empty(context.Candidates);
        }
        else
            Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeleteCandidate_WrongProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        context.VotingPrograms.Add(Program(2, "owner-1"));
        context.Candidates.Add(new Candidate { Id = 2, ProgramId = 2, Name = "Candidate", CandidateCode = "A" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        Assert.IsType<NotFoundResult>(await controller.DeleteCandidate(2, 1));
    }

    [Fact]
    public async Task Results_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.GetProgramWithCandidatesAsync(1)).ReturnsAsync((VotingProgram?)null);

        Assert.IsType<NotFoundResult>(await CreateController(context, services.Object).Results(1));
    }

    [Fact]
    public async Task Results_OtherOwner_ReturnsForbid()
    {
        await using var context = CreateContext();
        var program = Program(1, "owner-1");
        var services = new Mock<IVotingService>();
        services.Setup(x => x.GetProgramWithCandidatesAsync(1)).ReturnsAsync(program);
        var controller = CreateController(context, services.Object);
        SetUser(controller, "owner-2");

        Assert.IsType<ForbidResult>(await controller.Results(1));
    }

    [Fact]
    public async Task Results_OwnerLoadsResultsAndBlockchainStatus()
    {
        await using var context = CreateContext();
        var program = Program(1, "owner-1");
        var services = new Mock<IVotingService>();
        services.Setup(x => x.GetProgramWithCandidatesAsync(1)).ReturnsAsync(program);
        services.Setup(x => x.GetResultsAsync(1)).ReturnsAsync(new List<CandidateResultViewModel>());
        var blockchain = new Mock<IBlockchainService>();
        blockchain.Setup(x => x.IsChainValidForProgramAsync(context, 1)).ReturnsAsync(true);
        var controller = CreateController(context, services.Object, blockchain.Object);
        SetUser(controller, "owner-1");

        var result = Assert.IsType<ViewResult>(await controller.Results(1));

        Assert.Equal("~/Views/VotingAdmin/Results.cshtml", result.ViewName);
        Assert.True((bool)controller.ViewBag.BlockchainValid);
    }

    [Fact]
    public async Task ManageVoters_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();
        Assert.IsType<NotFoundResult>(await CreateController(context).ManageVoters(1));
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
    public async Task ManageVoters_OwnerLoadsVoters()
    {
        await using var context = CreateContext();
        context.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner", FullName = "Owner" });
        context.VotingPrograms.Add(Program(1, "owner-1"));
        context.Voters.Add(new Voter { Id = 4, ProgramId = 1, Name = "Voter" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = Assert.IsType<ViewResult>(await controller.ManageVoters(1));

        Assert.Equal("~/Views/ProgramOwner/ManageVoters.cshtml", result.ViewName);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Voter>>(result.Model));
    }

    [Fact]
    public async Task RegisterVoterGet_OwnerLoadsForm()
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = Assert.IsType<ViewResult>(await controller.RegisterVoter(1));

        Assert.Equal("~/Views/VotingAdmin/RegisterVoter.cshtml", result.ViewName);
        Assert.Equal(1, controller.ViewBag.ProgramId);
    }

    [Fact]
    public async Task RegisterVoterGet_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();

        Assert.IsType<NotFoundResult>(await CreateController(context).RegisterVoter(404));
    }

    [Fact]
    public async Task RegisterVoterPost_ServiceSuccess_Redirects()
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.RegisterVoterByEmailAsync(1, "Name", "email@example.com", null, "owner"))
            .ReturnsAsync((true, (string?)null));
        var controller = CreateController(context, services.Object);
        SetUser(controller, "owner-1");

        var result = await controller.RegisterVoter(1, "Name", "email@example.com", null);

        AssertRedirectToManageVoters(result, 1);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Fact]
    public async Task RegisterVoterPost_ServiceFailure_RedirectsWithError()
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.RegisterVoterByEmailAsync(1, "Name", "email@example.com", null, "owner"))
            .ReturnsAsync((false, (string?)"duplicate"));
        var controller = CreateController(context, services.Object);
        SetUser(controller, "owner-1");

        var result = await controller.RegisterVoter(1, "Name", "email@example.com", null);

        AssertRedirectToManageVoters(result, 1);
        Assert.Equal("duplicate", controller.TempData["Error"]);
    }

    [Fact]
    public async Task RemoveVoter_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();
        Assert.IsType<NotFoundResult>(await CreateController(context).RemoveVoter(1, 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveVoter_ReflectsServiceResult(bool success)
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var services = new Mock<IVotingService>();
        services.Setup(x => x.RemoveVoterAsync(5, 1)).ReturnsAsync((success, success ? null : "not found"));
        var controller = CreateController(context, services.Object);
        SetUser(controller, "owner-1");

        var result = await controller.RemoveVoter(5, 1);

        AssertRedirectToManageVoters(result, 1);
        Assert.Equal(success ? "Voter removed successfully." : "not found", controller.TempData[success ? "Success" : "Error"]);
    }

    [Fact]
    public async Task Delete_MissingProgram_ReturnsNotFound()
    {
        await using var context = CreateContext();
        Assert.IsType<NotFoundResult>(await CreateController(context).Delete(1));
    }

    [Fact]
    public async Task Delete_OtherOwner_ReturnsForbid()
    {
        await using var context = CreateContext();
        context.VotingPrograms.Add(Program(1, "owner-1"));
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-2");

        Assert.IsType<ForbidResult>(await controller.Delete(1));
    }

    [Fact]
    public async Task Delete_ActivePublishedProgram_IsRejected()
    {
        await using var context = CreateContext();
        var program = Program(1, "owner-1");
        program.IsPublished = true;
        program.StartTime = DateTime.UtcNow.AddMinutes(-1);
        program.EndTime = DateTime.UtcNow.AddMinutes(1);
        context.VotingPrograms.Add(program);
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = await controller.Delete(1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProgramOwnerController.MyPrograms), redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.NotNull(await context.VotingPrograms.FindAsync(1));
    }

    [Fact]
    public async Task Delete_EndedProgram_RemovesProgram()
    {
        await using var context = CreateContext();
        var program = Program(1, "owner-1");
        program.IsPublished = true;
        program.EndTime = DateTime.UtcNow.AddMinutes(-1);
        context.VotingPrograms.Add(program);
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        SetUser(controller, "owner-1");

        var result = await controller.Delete(1);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(await context.VotingPrograms.FindAsync(1));
    }

    private static VotingProgram Program(int id, string ownerId) => new()
    {
        Id = id,
        ProgramName = $"Program {id}",
        OwnerId = ownerId,
        StartTime = DateTime.UtcNow.AddHours(1),
        EndTime = DateTime.UtcNow.AddHours(2)
    };

    private static VotingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new VotingDbContext(options);
    }

    private static ProgramOwnerController CreateController(
        VotingDbContext context,
        IVotingService? votingService = null,
        IBlockchainService? blockchainService = null)
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Owner", UserName = "owner" });

        var httpContext = new DefaultHttpContext();
        var controller = new ProgramOwnerController(
            context,
            votingService ?? Mock.Of<IVotingService>(),
            blockchainService ?? Mock.Of<IBlockchainService>(),
            userManager.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        SetUser(controller, "owner-1");
        return controller;
    }

    private static void SetUser(Controller controller, string? userId, bool admin = false)
    {
        var claims = new List<Claim>();
        if (userId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (admin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static void AssertRedirectToManageCandidates(IActionResult result, int programId)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProgramOwnerController.ManageCandidates), redirect.ActionName);
        Assert.Equal(programId, redirect.RouteValues!["programId"]);
    }

    private static void AssertRedirectToManageVoters(IActionResult result, int programId)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProgramOwnerController.ManageVoters), redirect.ActionName);
        Assert.Equal(programId, redirect.RouteValues!["programId"]);
    }
}
