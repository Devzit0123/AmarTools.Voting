using System.Security.Claims;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AmarTools.Voting.Tests;

public class VotingServiceBoundaryTests
{
    private static VotingService CreateService()
    {
       

        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new VotingDbContext(options);


       

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "Test User")
        };

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: "TestAuthentication");

        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext
        {
            User = principal
        };


       
        var httpContextAccessor =
            new Mock<IHttpContextAccessor>();

        httpContextAccessor
            .Setup(x => x.HttpContext)
            .Returns(httpContext);


      

        var userStore =
            new Mock<IUserStore<ApplicationUser>>();

        var userManager =
            new Mock<UserManager<ApplicationUser>>(
                userStore.Object,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);


 

        var logger =
            new Mock<ILogger<VotingService>>();


      

        return new VotingService(
            context,
            httpContextAccessor.Object,
            userManager.Object,
            logger.Object);
    }


    private static VotingProgram CreateProgram(
        DateTime startTime,
        DateTime endTime)
    {
        return new VotingProgram
        {
            ProgramName = "BVA Test Program",
            Description = "Boundary value analysis test",
            StartTime = startTime,
            EndTime = endTime
        };
    }




    [Theory]
    [InlineData(4, 59, false)]
    [InlineData(5, 0, true)]
    [InlineData(5, 1, true)]
    public async Task ProgramDuration_BoundaryValues(
        int minutes,
        int seconds,
        bool expectedValid)
    {
        

        var service = CreateService();

        var startTime = DateTime.UtcNow;

        var endTime = startTime
            .AddMinutes(minutes)
            .AddSeconds(seconds);

        var model = CreateProgram(
            startTime,
            endTime);



        var result =
            await service.ValidateAndCreateProgramAsync(model);



        Assert.Equal(
            expectedValid,
            result.success);
    }
}
