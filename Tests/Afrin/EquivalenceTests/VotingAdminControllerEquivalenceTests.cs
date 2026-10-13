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



public class VotingAdminControllerEquivalenceTests
{
    private static (VotingAdminController controller, VotingDbContext context) CreateController()
    {
        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new VotingDbContext(options);

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);

        var controller = new VotingAdminController(
            context,
            new Mock<IBlockchainService>().Object,
            new Mock<IVotingService>().Object,
            userManager.Object);

        var httpContext = new DefaultHttpContext();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        controller.TempData = new TempDataDictionary(
            httpContext,
            Mock.Of<ITempDataProvider>());

        return (controller, context);
    }

    [Theory]
    [InlineData(false, -10, 60, true)]     
    [InlineData(true, -10, 60, false)]     
    [InlineData(true, 60, 120, false)]     
    [InlineData(true, -120, -60, true)]    
    public async Task Delete_ProgramStatePartitions(
        bool isPublished,
        int startMinutesFromNow,
        int endMinutesFromNow,
        bool expectedDeleted)
    {
        var (controller, context) = CreateController();

        context.VotingPrograms.Add(new VotingProgram
        {
            Id = 10,
            ProgramName = "EP Program",
            IsPublished = isPublished,
            StartTime = DateTime.UtcNow.AddMinutes(startMinutesFromNow),
            EndTime = DateTime.UtcNow.AddMinutes(endMinutesFromNow)
        });
        await context.SaveChangesAsync();

        var result = await controller.Delete(10);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var stillThere = await context.VotingPrograms.FindAsync(10);

        if (expectedDeleted)
            Assert.Null(stillThere);
        else
            Assert.NotNull(stillThere);

        await context.DisposeAsync();
    }

    [Fact]
    public async Task Delete_UnknownProgram_ReturnsNotFound()       // EP5
    {
        var (controller, context) = CreateController();

        var result = await controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);

        await context.DisposeAsync();
    }
}