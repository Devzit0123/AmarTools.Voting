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

// ================================================================
// EQUIVALENCE PARTITIONING (EP) - VotingService
//
// One representative value is tested from each class of inputs
// that the service treats the same way.
// ================================================================

public class VotingServiceEquivalenceTests
{
    private sealed record Fixture(
        VotingService Service,
        VotingDbContext Context,
        Mock<UserManager<ApplicationUser>> UserManager);

    private static Fixture CreateFixture(
        bool authenticated = true,
        string? userId = "test-user-id",
        string userName = "Test User",
        bool isAdmin = false)
    {
        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new VotingDbContext(options);

        var claims = new List<Claim>();
        if (userId != null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        claims.Add(new Claim(ClaimTypes.Name, userName));
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        // No authenticationType => IsAuthenticated is false
        var identity = authenticated
            ? new ClaimsIdentity(claims, "TestAuthentication")
            : new ClaimsIdentity(claims);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);

        userManager
            .Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var logger = new Mock<ILogger<VotingService>>();

        var service = new VotingService(
            context,
            accessor.Object,
            userManager.Object,
            logger.Object);

        return new Fixture(service, context, userManager);
    }

    private static VotingProgram NewProgram(
        DateTime start,
        DateTime end,
        string? slug = null) => new()
    {
        ProgramName = "EP Test Program",
        StartTime = start,
        EndTime = end,
        Slug = slug
    };

    private static async Task<VotingProgram> SeedProgramAsync(
        VotingDbContext context,
        int id = 1,
        string ownerId = "owner-1")
    {
        var program = new VotingProgram
        {
            Id = id,
            ProgramName = "Seeded Program",
            OwnerId = ownerId,
            StartTime = DateTime.UtcNow.AddHours(1),
            EndTime = DateTime.UtcNow.AddHours(2)
        };

        context.VotingPrograms.Add(program);
        await context.SaveChangesAsync();
        return program;
    }


    // ============================================================
    // ValidateAndCreateProgramAsync
    //
    // User state
    //   EP1  not authenticated            -> rejected
    //   EP2  authenticated, no user id    -> rejected
    //   EP3  authenticated with user id   -> continues to validation
    //
    // Program name
    //   EP4  null / empty / whitespace    -> rejected
    //   EP5  real text                    -> accepted
    //
    // Start / end time
    //   EP6  default (not selected)       -> rejected
    //   EP7  not UTC and no browser offset-> rejected
    //   EP8  UTC, end before start        -> rejected
    //   EP9  UTC, valid duration          -> accepted
    //   EP10 not UTC but offset supplied  -> accepted, converted to UTC
    // ============================================================

    [Fact]
    public async Task Create_UnauthenticatedUser_IsRejected()      // EP1
    {
        var f = CreateFixture(authenticated: false);
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1)));

        Assert.False(result.success);
        Assert.Contains("logged in", result.errorMessage);
    }

    [Fact]
    public async Task Create_AuthenticatedWithoutUserId_IsRejected()   // EP2
    {
        var f = CreateFixture(userId: null);
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1)));

        Assert.False(result.success);
        Assert.Contains("Unable to determine", result.errorMessage);
    }

    [Theory]
    [InlineData("")]        // EP4
    [InlineData("   ")]     // EP4
    [InlineData(null)]      // EP4
    public async Task Create_BlankProgramName_IsRejected(string? name)
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;
        var model = NewProgram(start, start.AddHours(1));
        model.ProgramName = name!;

        var result = await f.Service.ValidateAndCreateProgramAsync(model);

        Assert.False(result.success);
        Assert.Contains("Program name is required", result.errorMessage);
    }

    [Fact]
    public async Task Create_DefaultTimes_AreRejected()            // EP6
    {
        var f = CreateFixture();
        var model = new VotingProgram { ProgramName = "EP Test Program" };

        var result = await f.Service.ValidateAndCreateProgramAsync(model);

        Assert.False(result.success);
        Assert.Contains("valid start and end", result.errorMessage);
    }

    [Fact]
    public async Task Create_NonUtcTimesWithoutOffset_AreRejected()    // EP7
    {
        var f = CreateFixture();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var model = NewProgram(start, start.AddHours(1));

        var result = await f.Service.ValidateAndCreateProgramAsync(model);

        Assert.False(result.success);
        Assert.Contains("time-zone", result.errorMessage);
    }

    [Fact]
    public async Task Create_EndBeforeStart_IsRejected()           // EP8
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;
        var model = NewProgram(start, start.AddMinutes(-10));

        var result = await f.Service.ValidateAndCreateProgramAsync(model);

        Assert.False(result.success);
        Assert.Contains("at least 5 minutes", result.errorMessage);
    }

    [Fact]
    public async Task Create_ValidUtcProgram_IsSavedWithOwner()    // EP3 + EP5 + EP9
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1)));

        Assert.True(result.success);
        Assert.Null(result.errorMessage);

        var saved = await f.Context.VotingPrograms.SingleAsync();
        Assert.Equal("test-user-id", saved.OwnerId);
        Assert.Equal("Test User", saved.CreatedBy);
    }

    [Fact]
    public async Task Create_NonUtcWithOffset_IsConvertedToUtc()   // EP10
    {
        var f = CreateFixture();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var model = NewProgram(start, start.AddHours(1));
        model.StartTimeOffsetMinutes = 360;   // UTC+6
        model.EndTimeOffsetMinutes = 360;

        var result = await f.Service.ValidateAndCreateProgramAsync(model);

        Assert.True(result.success);

        var saved = await f.Context.VotingPrograms.SingleAsync();
        Assert.Equal(new DateTime(2026, 1, 1, 6, 0, 0), saved.StartTime);
        Assert.Equal(new DateTime(2026, 1, 1, 7, 0, 0), saved.EndTime);
    }



    [Theory]
    [InlineData(null, null)]                              // EP1
    [InlineData("", null)]                                // EP1
    [InlineData("   ", null)]                             // EP1
    [InlineData("campus-2026", "campus-2026")]            // EP2
    [InlineData("My Election 2026!", "my-election-2026")] // EP3
    [InlineData("UPPER_case", "upper_case")]              // EP3
    [InlineData("!!!", null)]                             // EP4
    public async Task Create_SlugPartitions_AreNormalized(
        string? rawSlug,
        string? expectedSlug)
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1), rawSlug));

        Assert.True(result.success);

        var saved = await f.Context.VotingPrograms.SingleAsync();
        Assert.Equal(expectedSlug, saved.Slug);
    }

    [Fact]
    public async Task Create_DuplicateSlug_IsRejected()            // EP5
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var first = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1), "taken"));

        var second = await f.Service.ValidateAndCreateProgramAsync(
            NewProgram(start, start.AddHours(1), "TAKEN"));

        Assert.True(first.success);
        Assert.False(second.success);
        Assert.Contains("already taken", second.errorMessage);
    }



    [Fact]
    public async Task Update_MissingProgram_IsRejected()           // EP1
    {
        var f = CreateFixture();
        var start = DateTime.UtcNow;

        var result = await f.Service.ValidateAndUpdateProgramAsync(
            999, NewProgram(start, start.AddHours(1)));

        Assert.False(result.success);
        Assert.Contains("not found", result.errorMessage);
    }

    [Fact]
    public async Task Update_ByOwner_IsAccepted()                  // EP2
    {
        var f = CreateFixture(userId: "owner-1");
        await SeedProgramAsync(f.Context, ownerId: "owner-1");

        var start = DateTime.UtcNow;
        var model = NewProgram(start, start.AddHours(2));
        model.ProgramName = "Updated By Owner";

        var result = await f.Service.ValidateAndUpdateProgramAsync(1, model);

        Assert.True(result.success);

        var saved = await f.Context.VotingPrograms.FindAsync(1);
        Assert.Equal("Updated By Owner", saved!.ProgramName);
    }

    [Fact]
    public async Task Update_ByDifferentNonAdminUser_IsRejected()  // EP3
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

    [Fact]
    public async Task Update_ByAdminWhoIsNotOwner_IsAccepted()     // EP4
    {
        var f = CreateFixture(userId: "admin-1", isAdmin: true);
        await SeedProgramAsync(f.Context, ownerId: "owner-1");

        var start = DateTime.UtcNow;
        var model = NewProgram(start, start.AddHours(2));
        model.ProgramName = "Updated By Admin";

        var result = await f.Service.ValidateAndUpdateProgramAsync(1, model);

        Assert.True(result.success);

        var saved = await f.Context.VotingPrograms.FindAsync(1);
        Assert.Equal("Updated By Admin", saved!.ProgramName);
    }


    [Fact]
    public void NormalizeToUtc_UtcKind_IsUnchanged()               // EP1
    {
        var value = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

        var result = VotingService.NormalizeToUtc(value, 360);

        Assert.Equal(value, result);
    }

    [Fact]
    public void NormalizeToUtc_LocalKind_UsesToUniversalTime()     // EP2
    {
        var value = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Local);

        var result = VotingService.NormalizeToUtc(value, null);

        Assert.Equal(value.ToUniversalTime(), result);
    }

    [Theory]
    [InlineData(330, 6, 30)]    
    [InlineData(-480, 20, 0)]    
    [InlineData(0, 12, 0)]       
    public void NormalizeToUtc_UnspecifiedWithOffset_AppliesOffset(
        int offsetMinutes,
        int expectedHour,
        int expectedMinute)
    {
        var value = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = VotingService.NormalizeToUtc(value, offsetMinutes);

        Assert.Equal(new DateTime(2026, 1, 1, expectedHour, expectedMinute, 0), result);
    }

    [Fact]
    public void NormalizeToUtc_UnspecifiedWithoutOffset_Throws()   // EP6
    {
        var value = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(
            () => VotingService.NormalizeToUtc(value, null));
    }


    [Theory]
    [InlineData("")]        // EP1
    [InlineData("   ")]     // EP1
    public async Task Register_BlankName_IsRejected(string name)
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, name, "student@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Contains("required", result.errorMessage);
    }

    [Theory]
    [InlineData("")]        // EP2
    [InlineData("   ")]     // EP2
    public async Task Register_BlankEmail_IsRejected(string email)
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", email, null, "owner");

        Assert.False(result.success);
        Assert.Contains("required", result.errorMessage);
    }

    [Fact]
    public async Task Register_MissingProgram_IsRejected()         // EP3
    {
        var f = CreateFixture();

        var result = await f.Service.RegisterVoterByEmailAsync(
            999, "Karim Hossain", "student@example.com", null, "owner");

        Assert.False(result.success);
        Assert.Contains("not found", result.errorMessage);
    }

    [Fact]
    public async Task Register_NewEmail_IsStoredCleaned()          // EP4 + EP7
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "  Karim Hossain ", " Student@Example.com ", null, "owner");

        Assert.True(result.success);

        var voter = await f.Context.Voters.SingleAsync();
        Assert.Equal("Karim Hossain", voter.Name);
        Assert.Equal("student@example.com", voter.Email);
        Assert.Equal("owner", voter.RegistrationSource);
        Assert.Null(voter.UserId);
    }

    [Fact]
    public async Task Register_EmailAlreadyInProgram_IsRejected()  // EP5
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

    [Fact]
    public async Task Register_EmailUsedOnlyInOtherProgram_IsAccepted()   // EP6
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context, id: 1);
        await SeedProgramAsync(f.Context, id: 2);

        f.Context.Voters.Add(new Voter
        {
            Id = 5,
            Name = "Existing Voter",
            Email = "student@example.com",
            ProgramId = 1
        });
        await f.Context.SaveChangesAsync();

        var result = await f.Service.RegisterVoterByEmailAsync(
            2, "Karim Hossain", "student@example.com", null, "owner");

        Assert.True(result.success);
        Assert.Equal(2, await f.Context.Voters.CountAsync());
    }

    [Fact]
    public async Task Register_EmailWithExistingAccount_LinksUserId()     // EP8
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

    [Fact]
    public async Task Register_AccountAlreadyRegisteredUnderOtherEmail_IsRejected()  // EP9
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
        Assert.Contains("user account", result.errorMessage);
        Assert.Equal(1, await f.Context.Voters.CountAsync());
    }

    [Theory]
    [InlineData(null, null)]            // EP10
    [InlineData("   ", null)]           // EP10
    [InlineData(" M-100 ", "M-100")]    // EP11
    public async Task Register_MemberIdPartitions(
        string? memberId,
        string? expectedStored)
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RegisterVoterByEmailAsync(
            1, "Karim Hossain", "student@example.com", memberId, "owner");

        Assert.True(result.success);

        var voter = await f.Context.Voters.SingleAsync();
        Assert.Equal(expectedStored, voter.MemberId);
    }


    [Fact]
    public async Task RemoveVoter_ExistsInProgram_IsRemoved()      // EP1
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        f.Context.Voters.Add(new Voter { Id = 5, Name = "Karim Hossain", ProgramId = 1 });
        await f.Context.SaveChangesAsync();

        var result = await f.Service.RemoveVoterAsync(5, 1);

        Assert.True(result.success);
        Assert.Equal(0, await f.Context.Voters.CountAsync());
    }

    [Fact]
    public async Task RemoveVoter_UnknownId_IsRejected()           // EP2
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var result = await f.Service.RemoveVoterAsync(999, 1);

        Assert.False(result.success);
        Assert.Contains("not found", result.errorMessage);
    }

    [Fact]
    public async Task RemoveVoter_BelongsToOtherProgram_IsRejected()   // EP3
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




    [Fact]
    public async Task Results_MissingProgram_ReturnsEmpty()        
    {
        var f = CreateFixture();

        var results = await f.Service.GetResultsAsync(999);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Results_ProgramWithoutCandidates_ReturnsEmpty()  
    {
        var f = CreateFixture();
        await SeedProgramAsync(f.Context);

        var results = await f.Service.GetResultsAsync(1);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Results_CandidatesWithoutVotes_AllZero()     
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

    [Fact]
    public async Task Results_MixedVotes_AreSortedDescending()     
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
}