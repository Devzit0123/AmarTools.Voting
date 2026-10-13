using System.ComponentModel.DataAnnotations;
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
using Microsoft.Extensions.Logging;
using Moq;

namespace AmarTools.Voting.Tests;


public class TestCaseExecutionTests
{
   

    private sealed record Fixture(
        VotingService Service,
        VotingDbContext Context,
        Mock<UserManager<ApplicationUser>> UserManager);

    private static Fixture CreateFixture(
        bool authenticated = true,
        string userId = "test-user-id",
        bool isAdmin = false)
    {
        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new VotingDbContext(options);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "Test User")
        };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var identity = authenticated
            ? new ClaimsIdentity(claims, "TestAuthentication")
            : new ClaimsIdentity(claims);

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext)
                .Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);

        userManager
            .Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var service = new VotingService(
            context,
            accessor.Object,
            userManager.Object,
            new Mock<ILogger<VotingService>>().Object);

        return new Fixture(service, context, userManager);
    }

    private static VotingProgram NewProgram(
        DateTime start,
        DateTime end,
        string? slug = null) => new()
    {
        ProgramName = "TC Program",
        StartTime = start,
        EndTime = end,
        Slug = slug
    };

    private static async Task SeedProgramAsync(
        VotingDbContext context,
        int id = 1,
        string ownerId = "owner-1")
    {
        context.VotingPrograms.Add(new VotingProgram
        {
            Id = id,
            ProgramName = "Seeded Program",
            OwnerId = ownerId,
            StartTime = DateTime.UtcNow.AddHours(1),
            EndTime = DateTime.UtcNow.AddHours(2)
        });

        await context.SaveChangesAsync();
    }

    private static bool IsValid(object model)
    {
        var results = new List<ValidationResult>();

        return Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);
    }

    private static VotingProgram ValidModelProgram() => new()
    {
        ProgramName = "Campus Election",
        StartTime = DateTime.UtcNow,
        EndTime = DateTime.UtcNow.AddHours(1)
    };

    private static (VotingAdminController controller, VotingDbContext context) CreateAdminController()
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

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return (controller, context);
    }



    // TC01  Functional
    [Fact]
    public async Task TC01_CreateValidProgram_IsSavedWithOwner()
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1)));

        Assert.True(result.success);

        var saved = await f.Context.VotingPrograms.SingleAsync();
        Assert.Equal("test-user-id", saved.OwnerId);
        Assert.Equal("Test User", saved.CreatedBy);
    }

    // TC02  
    [Fact]
    public async Task TC02_CreateProgram_EmptyName_IsRejected()
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;
        var model = NewProgram(start, start.AddHours(1));
        model.ProgramName = "";

        var result = await f.Service.ValidateAndCreateProgramAsync(model);

        Assert.False(result.success);
        Assert.Contains("Program name is required", result.errorMessage);
    }

    // TC03  
    [Fact]
    public async Task TC03_CreateProgram_DurationExactlyFiveMinutes_IsAccepted()
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddMinutes(5)));

        Assert.True(result.success);
    }

    // TC04  
    [Fact]
    public async Task TC04_CreateProgram_DurationJustUnderFiveMinutes_IsRejected()
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddMinutes(4).AddSeconds(59)));

        Assert.False(result.success);
        Assert.Contains("at least 5 minutes", result.errorMessage);
    }

    // TC05  
    [Fact]
    public async Task TC05_CreateProgram_EndBeforeStart_IsRejected()
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddMinutes(-10)));

        Assert.False(result.success);
        Assert.Contains("at least 5 minutes", result.errorMessage);
    }

    // TC06  
    [Fact]
    public async Task TC06_CreateProgram_UnauthenticatedUser_IsRejected()
    {
        var f = CreateFixture(authenticated: false);
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1)));

        Assert.False(result.success);
        Assert.Contains("logged in", result.errorMessage);
    }

    // TC07  
    [Fact]
    public async Task TC07_CreateProgram_MessySlug_IsNormalized()
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1), "Campus Vote 2026"));

        Assert.True(result.success);

        var saved = await f.Context.VotingPrograms.SingleAsync();
        Assert.Equal("campus-vote-2026", saved.Slug);
    }

    // TC08  
    [Fact]
    public async Task TC08_CreateProgram_DuplicateSlug_IsRejected()
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1), "taken"));

        var second = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1), "TAKEN"));

        Assert.False(second.success);
        Assert.Contains("already taken", second.errorMessage);
    }

    // TC09  
    [Fact]
    public async Task TC09_UpdateProgram_ByOwner_IsAccepted()
    {
        var f = CreateFixture(userId: "owner-1");
        await SeedProgramAsync(f.Context, ownerId: "owner-1");

        var start = DateTime.UtcNow;
        var model = NewProgram(start, start.AddHours(2));
        model.ProgramName = "Renamed By Owner";

        var result = await f.Service.ValidateAndUpdateProgramAsync(1, model);

        Assert.True(result.success);

        var saved = await f.Context.VotingPrograms.FindAsync(1);
        Assert.Equal("Renamed By Owner", saved!.ProgramName);
    }

    // TC10  
    [Fact]
    public async Task TC10_UpdateProgram_ByOtherUser_IsRejected()
    {
        var f = CreateFixture(userId: "someone-else");
        await SeedProgramAsync(f.Context, ownerId: "owner-1");

        var start = DateTime.UtcNow;
        var model = NewProgram(start, start.AddHours(2));
        model.ProgramName = "Hijacked";

        var result = await f.Service.ValidateAndUpdateProgramAsync(1, model);

        Assert.False(result.success);
        Assert.Contains("not allowed", result.errorMessage);

        var saved = await f.Context.VotingPrograms.FindAsync(1);
        Assert.Equal("Seeded Program", saved!.ProgramName);
    }

    // TC11  
    [Fact]
    public async Task TC11_UpdateProgram_ByAdmin_IsAccepted()
    {
        var f = CreateFixture(userId: "admin-1", isAdmin: true);
        await SeedProgramAsync(f.Context, ownerId: "owner-1");

        var start = DateTime.UtcNow;
        var model = NewProgram(start, start.AddHours(2));
        model.ProgramName = "Renamed By Admin";

        var result = await f.Service.ValidateAndUpdateProgramAsync(1, model);

        Assert.True(result.success);
    }

    // TC12  
    [Fact]
    public async Task TC12_UpdateProgram_MissingProgram_IsRejected()
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndUpdateProgramAsync(
            999, NewProgram(start, start.AddHours(1)));

        Assert.False(result.success);
        Assert.Contains("not found", result.errorMessage);
    }



    // TC13  
    [Fact]
    public async Task TC13_RegisterVoter_Valid_EmailIsCleaned()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", " Student@Example.com ", null, "owner");

        Assert.True(result.success);

        var voter = await f.Context.Voters.SingleAsync();
        Assert.Equal("student@example.com", voter.Email);
    }

    // TC14  
    [Fact]
    public async Task TC14_RegisterVoter_BlankName_IsRejected()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "   ", "student@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Contains("required", result.errorMessage);
    }

    // TC15  
    [Fact]
    public async Task TC15_RegisterVoter_DuplicateEmail_IsRejected()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.Context.Voters.Add(new Voter
        {
            Id = 5,
            Name = "Existing Voter",
            Email = "student@example.com",
            ProgramId = 1
        });
        await f.Context.SaveChangesAsync();

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "STUDENT@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Contains("email is already registered", result.errorMessage);
        Assert.Equal(1, await f.Context.Voters.CountAsync());
    }

    // TC16  
    [Fact]
    public async Task TC16_RegisterVoter_MissingProgram_IsRejected()
    {
        var f = CreateFixture();

        var result = await f.Service.RegisterVoterByEmailAsync(
            999, "Karim Hossain", "student@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Contains("not found", result.errorMessage);
    }

    // TC17  
    [Fact]
    public async Task TC17_RegisterVoter_EmailWithAccount_LinksUserId()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.UserManager
            .Setup(x => x.FindByEmailAsync("student@example.com"))
            .ReturnsAsync(new ApplicationUser
            {
                Id = "user-1",
                UserName = "student@example.com",
                Email = "student@example.com"
            });

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "student@example.com", null, "owner");

        Assert.True(result.success);

        var voter = await f.Context.Voters.SingleAsync();
        Assert.Equal("user-1", voter.UserId);
    }

    // TC18  
    [Fact]
    public async Task TC18_RemoveVoter_ExistingVoter_IsRemoved()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.Context.Voters.Add(new Voter { Id = 5, Name = "Karim Hossain", ProgramId = 1 });
        await f.Context.SaveChangesAsync();

        var result = await f.Service.RemoveVoterAsync(5, 1);

        Assert.True(result.success);
        Assert.Equal(0, await f.Context.Voters.CountAsync());
    }

    // TC19  
    [Fact]
    public async Task TC19_RemoveVoter_WrongProgram_IsRejected()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context, id: 1);
        await SeedProgramAsync(f.Context, id: 2);

        f.Context.Voters.Add(new Voter { Id = 5, Name = "Karim Hossain", ProgramId = 2 });
        await f.Context.SaveChangesAsync();

        var result = await f.Service.RemoveVoterAsync(5, 1);

        Assert.False(result.success);
        Assert.Equal(1, await f.Context.Voters.CountAsync());
    }


    

    // TC20  
    [Fact]
    public async Task TC20_Results_MissingProgram_ReturnsEmptyList()
    {
        var f = CreateFixture();

        var results = await f.Service.GetResultsAsync(999);

        Assert.Empty(results);
    }

    // TC21  
    [Fact]
    public async Task TC21_Results_AreSortedByVotesDescending()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.Context.Candidates.AddRange(
            new Candidate { Id = 10, ProgramId = 1, Name = "Candidate A", CandidateCode = "A" },
            new Candidate { Id = 11, ProgramId = 1, Name = "Candidate B", CandidateCode = "B" },
            new Candidate { Id = 12, ProgramId = 1, Name = "Candidate C", CandidateCode = "C" });

        f.Context.Votes.AddRange(
            new Vote { Id = 1, ProgramId = 1, CandidateId = 11, VoterId = 1 },
            new Vote { Id = 2, ProgramId = 1, CandidateId = 11, VoterId = 2 },
            new Vote { Id = 3, ProgramId = 1, CandidateId = 11, VoterId = 3 },
            new Vote { Id = 4, ProgramId = 1, CandidateId = 10, VoterId = 4 });
        await f.Context.SaveChangesAsync();

        var results = await f.Service.GetResultsAsync(1);

        Assert.Equal(new[] { 11, 10, 12 }, results.Select(r => r.Candidate.Id).ToArray());
        Assert.Equal(new[] { 3, 1, 0 }, results.Select(r => r.VoteCount).ToArray());
    }

    // TC22  
    [Fact]
    public async Task TC22_Results_NoVotes_AllCountsAreZero()
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.Context.Candidates.AddRange(
            new Candidate { Id = 10, ProgramId = 1, Name = "Candidate A", CandidateCode = "A" },
            new Candidate { Id = 11, ProgramId = 1, Name = "Candidate B", CandidateCode = "B" });
        await f.Context.SaveChangesAsync();

        var results = await f.Service.GetResultsAsync(1);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(0, r.VoteCount));
    }

    // MODEL RULES AND TIME CONVERSION
    //

    // TC23  
    [Fact]
    public void TC23_ProgramName_ThreeCharacters_IsValid()
    {
        var model = ValidModelProgram();
        model.ProgramName = new string('A', 3);

        Assert.True(IsValid(model));
    }

    // TC24  
    [Fact]
    public void TC24_ProgramName_TwoHundredOneCharacters_IsInvalid()
    {
        var model = ValidModelProgram();
        model.ProgramName = new string('A', 201);

        Assert.False(IsValid(model));
    }

    // TC25  
    [Fact]
    public void TC25_Slug_WithSpace_IsInvalid()
    {
        var model = ValidModelProgram();
        model.Slug = "campus election";

        Assert.False(IsValid(model));
    }

    // TC26  
    [Fact]
    public void TC26_IsOpenAt_ExactlyAtEndTime_IsFalse()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);

        Assert.False(VotingProgram.IsOpenAt(start, end, end));
    }

    // TC27  
    [Fact]
    public void TC27_NormalizeToUtc_AppliesBrowserOffset()
    {
        var local = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var utc = VotingService.NormalizeToUtc(local, 360);

        Assert.Equal(new DateTime(2026, 1, 1, 6, 0, 0), utc);
    }


    // ADMIN CONTROLLER

    // TC28  
    [Fact]
    public async Task TC28_Delete_PublishedRunningProgram_IsBlocked()
    {
        var (controller, context) = CreateAdminController();

        context.VotingPrograms.Add(new VotingProgram
        {
            Id = 10,
            ProgramName = "Running Program",
            IsPublished = true,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            EndTime = DateTime.UtcNow.AddHours(1)
        });
        await context.SaveChangesAsync();

        var result = await controller.Delete(10);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(await context.VotingPrograms.FindAsync(10));

        await context.DisposeAsync();
    }

    // TC29  
    [Fact]
    public async Task TC29_Delete_UnpublishedProgram_IsDeleted()
    {
        var (controller, context) = CreateAdminController();

        context.VotingPrograms.Add(new VotingProgram
        {
            Id = 10,
            ProgramName = "Draft Program",
            IsPublished = false,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1)
        });
        await context.SaveChangesAsync();

        var result = await controller.Delete(10);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Null(await context.VotingPrograms.FindAsync(10));

        await context.DisposeAsync();
    }

    // TC30  
    [Fact]
    public async Task TC30_Edit_MismatchedIds_ReturnsNotFound()
    {
        var (controller, context) = CreateAdminController();

        var model = new VotingProgram
        {
            Id = 10,
            ProgramName = "Mismatch Program",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1)
        };

        var result = await controller.Edit(9, model);

        Assert.IsType<NotFoundResult>(result);

        await context.DisposeAsync();
    }
}