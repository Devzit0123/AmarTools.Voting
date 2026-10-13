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
namespace AmarTools.Voting.Tests;
public class VotingAdminControllerTests
{
    private static (
        VotingAdminController controller,
        VotingDbContext context,
        Mock<IVotingService> votingService,
        Mock<IBlockchainService> blockchainService)
        CreateController()
    {
        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new VotingDbContext(options);
        var votingService = new Mock<IVotingService>();
        var blockchainService = new Mock<IBlockchainService>();
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        var controller = new VotingAdminController(
            context,
            blockchainService.Object,
            votingService.Object,
            userManager.Object);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(
            httpContext,
            Mock.Of<ITempDataProvider>());
        return (
            controller,
            context,
            votingService,
            blockchainService);
    }
    private static VotingProgram CreateProgram(int id)
    {
        return new VotingProgram
        {
            Id = id,
            ProgramName = "Test Program",
            Description = "Test description",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            IsPublished = false
        };
    }
    [Theory]
    [InlineData(9, 10, true)]
    [InlineData(10, 10, false)]
    [InlineData(11, 10, true)]
    public async Task Edit_IdBoundaryValues(
        int routeId,
        int modelId,
        bool expectedNotFound)
    {
        var (
            controller,
            context,
            votingService,
            _
        ) = CreateController();
        var model = CreateProgram(modelId);
        if (!expectedNotFound)
        {
            votingService
                .Setup(x => x.ValidateAndUpdateProgramAsync(
                    routeId,
                    It.IsAny<VotingProgram>()))
                .ReturnsAsync((true, (string?)null));
        }
        var result = await controller.Edit(routeId, model);
        if (expectedNotFound)
        {
            Assert.IsType<NotFoundResult>(result);
        }
        else
        {
            Assert.IsType<RedirectToActionResult>(result);
        }
        await context.DisposeAsync();
    }
    [Fact]
    public async Task EditGet_ExistingProgram_ReturnsView()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var program = CreateProgram(10);
        context.VotingPrograms.Add(program);
        await context.SaveChangesAsync();
        var result = await controller.Edit(10);
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("CreateProgram", viewResult.ViewName);
        Assert.NotNull(viewResult.Model);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task EditGet_MissingProgram_ReturnsNotFound()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var result = await controller.Edit(999);
        Assert.IsType<NotFoundResult>(result);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task ManageCandidates_ExistingProgram_ReturnsView()
    {
        var (
            controller,
            context,
            votingService,
            _
        ) = CreateController();
        var program = CreateProgram(10);
        votingService
            .Setup(x => x.GetProgramWithCandidatesAsync(10))
            .ReturnsAsync(program);
        var result = await controller.ManageCandidates(10);
        Assert.IsType<ViewResult>(result);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task ManageCandidates_MissingProgram_ReturnsNotFound()
    {
        var (
            controller,
            context,
            votingService,
            _
        ) = CreateController();
        votingService
            .Setup(x => x.GetProgramWithCandidatesAsync(999))
            .ReturnsAsync((VotingProgram?)null);
        var result = await controller.ManageCandidates(999);
        Assert.IsType<NotFoundResult>(result);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task ManageVoters_ExistingProgram_ReturnsView()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var owner = new ApplicationUser
        {
            Id = "owner-1",
            UserName = "owner@test.com",
            Email = "owner@test.com"
        };
        var program = CreateProgram(10);
        program.OwnerId = owner.Id;
        program.Owner = owner;
        context.Users.Add(owner);
        context.VotingPrograms.Add(program);
        await context.SaveChangesAsync();
        var result = await controller.ManageVoters(10);
        Assert.IsType<ViewResult>(result);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task ManageVoters_MissingProgram_ReturnsNotFound()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var result = await controller.ManageVoters(999);
        Assert.IsType<NotFoundResult>(result);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task RegisterVoterGet_ExistingProgram_ReturnsView()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var program = CreateProgram(10);
        context.VotingPrograms.Add(program);
        await context.SaveChangesAsync();
        var result = await controller.RegisterVoter(10);
        Assert.IsType<ViewResult>(result);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task RegisterVoterGet_MissingProgram_ReturnsNotFound()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var result = await controller.RegisterVoter(999);
        Assert.IsType<NotFoundResult>(result);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task Delete_MissingProgram_ReturnsNotFound()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var result = await controller.Delete(999);
        Assert.IsType<NotFoundResult>(result);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task Delete_ExistingUnpublishedProgram_RedirectsToIndex()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var program = CreateProgram(10);
        context.VotingPrograms.Add(program);
        await context.SaveChangesAsync();
        var result = await controller.Delete(10);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        var deletedProgram =
            await context.VotingPrograms.FindAsync(10);
        Assert.Null(deletedProgram);
        await context.DisposeAsync();
    }
    [Fact]
    public async Task Delete_PublishedProgramThatHasNotEnded_IsBlocked()
    {
        var (
            controller,
            context,
            _,
            _
        ) = CreateController();
        var program = CreateProgram(10);
        program.IsPublished = true;
        program.StartTime = DateTime.UtcNow.AddMinutes(-10);
        program.EndTime = DateTime.UtcNow.AddHours(1);
        context.VotingPrograms.Add(program);
        await context.SaveChangesAsync();
        var result = await controller.Delete(10);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        var stillExists =
            await context.VotingPrograms.FindAsync(10);
        Assert.NotNull(stillExists);
        await context.DisposeAsync();
    }
}