using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AmarTools.Voting.Tests;

public class RegisterVoterBasisPathTests
{
    
    private sealed class FailingSaveContext(DbContextOptions<VotingDbContext> options)
        : VotingDbContext(options)
    {
        public bool FailOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailOnSave)
                throw new DbUpdateException("Simulated database failure.");

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed record Fixture(
        VotingService Service,
        VotingDbContext Context,
        Mock<UserManager<ApplicationUser>> UserManager);

    private static DbContextOptions<VotingDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static Fixture CreateFixture(VotingDbContext? context = null)
    {
        context ??= new VotingDbContext(NewOptions());

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);

        userManager
            .Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var service = new VotingService(
            context,
            new Mock<IHttpContextAccessor>().Object,
            userManager.Object,
            new Mock<ILogger<VotingService>>().Object);

        return new Fixture(service, context, userManager);
    }

    private static async Task SeedProgramAsync(VotingDbContext context, int id = 1)
    {
        context.VotingPrograms.Add(new VotingProgram
        {
            Id = id,
            ProgramName = "Basis Path Program",
            OwnerId = "owner-1",
            StartTime = DateTime.UtcNow.AddHours(1),
            EndTime = DateTime.UtcNow.AddHours(2)
        });

        await context.SaveChangesAsync();
    }


    // BP1: 1-2-4-6-8-11-13-14-15
    [Fact]
    public async Task BP1_HappyPath_NoLinkedUser_WithMemberId()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "karim@example.com", "M-1", "owner");

        Assert.True(result.success);
        Assert.Null(result.errorMessage);

        var voter = await f.Context.Voters.SingleAsync();
        Assert.Equal("M-1", voter.MemberId);
        Assert.Null(voter.UserId);
    }

    // BP2: 1-3
    [Fact]
    public async Task BP2_BlankName()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "", "karim@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Equal("Name and Email are required.", result.errorMessage);
        Assert.Equal(0, await f.Context.Voters.CountAsync());
    }

    // BP3: 1-2-3
    [Fact]
    public async Task BP3_BlankEmail()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "   ", null, "owner");

        Assert.False(result.success);
        Assert.Equal("Name and Email are required.", result.errorMessage);
        Assert.Equal(0, await f.Context.Voters.CountAsync());
    }

    // BP4: 1-2-4-5
    [Fact]
    public async Task BP4_ProgramNotFound()
    {
        var f = CreateFixture();

        var result = await f.Service.RegisterVoterByEmailAsync(
            999, "Karim Hossain", "karim@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Equal("Voting program not found.", result.errorMessage);
    }

    // BP5: 1-2-4-6-7
    [Fact]
    public async Task BP5_EmailAlreadyRegistered()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.Context.Voters.Add(new Voter
        {
            Id = 5,
            Name = "Existing Voter",
            Email = "karim@example.com",
            ProgramId = 1
        });
        await f.Context.SaveChangesAsync();

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "karim@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Contains("email is already registered", result.errorMessage);
        Assert.Equal(1, await f.Context.Voters.CountAsync());
    }

    // BP6: 1-2-4-6-8-9-10
    [Fact]
    public async Task BP6_LinkedUserAlreadyRegistered()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.Context.Voters.Add(new Voter
        {
            Id = 5,
            Name = "Existing Voter",
            Email = "first@example.com",
            UserId = "user-1",
            ProgramId = 1
        });
        await f.Context.SaveChangesAsync();

        f.UserManager
            .Setup(x => x.FindByEmailAsync("alias@example.com"))
            .ReturnsAsync(new ApplicationUser
            {
                Id = "user-1",
                UserName = "first@example.com",
                Email = "alias@example.com"
            });

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "alias@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Contains("user account is already registered", result.errorMessage);
        Assert.Equal(1, await f.Context.Voters.CountAsync());
    }

    // BP7: 1-2-4-6-8-9-11-13-14-15
    [Fact]
    public async Task BP7_LinkedUserNotYetRegistered()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.UserManager
            .Setup(x => x.FindByEmailAsync("karim@example.com"))
            .ReturnsAsync(new ApplicationUser
            {
                Id = "user-1",
                UserName = "karim@example.com",
                Email = "karim@example.com"
            });

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "karim@example.com", "M-2", "owner");

        Assert.True(result.success);

        var voter = await f.Context.Voters.SingleAsync();
        Assert.Equal("user-1", voter.UserId);
        Assert.Equal("M-2", voter.MemberId);
    }

    // BP8: 1-2-4-6-8-11-12-14-15
    [Fact]
    public async Task BP8_BlankMemberId()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "karim@example.com", null, "owner");

        Assert.True(result.success);

        var voter = await f.Context.Voters.SingleAsync();
        Assert.Null(voter.MemberId);
    }

    // BP9: 1-2-4-6-8-11-13-14-16
    [Fact]
    public async Task BP9_DatabaseUpdateException()
    {
        var context = new FailingSaveContext(NewOptions());
        var f = CreateFixture(context);

        await SeedProgramAsync(context);
        context.FailOnSave = true;   

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "karim@example.com", "M-3", "owner");

        Assert.False(result.success);
        Assert.Contains("could not be registered", result.errorMessage);
    }
}