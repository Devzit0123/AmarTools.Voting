using AmarTools.Voting.Areas.Identity.Pages.Account;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  Tests for the hardening changes made after the codebase assessment:
//    H01  Hash chain covers the vote's content (D18); legacy v1 blocks still validate
//    H02  Chain validation detects missing and duplicate blocks (D19)
//    H03  Background sweep re-queues votes without a block (D20)
//    H04  Voter slots are linked only to confirmed accounts (D15)
//    H05  New accounts are voters; an admin grants ProgramOwner
//    H06  Signed-in voters get a "My Elections" page instead of a 403
//    H07  Security headers
//    H08  Account email tokens
//
//    dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "FullyQualifiedName~HardeningTests"
// ─────────────────────────────────────────────────────────────────────────────
[Trait("Category", "Hardening")]
public sealed class HardeningTests
{
    // InMemory ignores transactions; BlockchainService opens one, so silence that warning.
    private static VotingDbContext CreateContext(string? name = null) =>
        new(new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static BlockchainService Chain() => new(NullLogger<BlockchainService>.Instance);

    private static Vote NewVote(int id, int candidateId, int voterId, DateTime? votedAt = null) => new()
    {
        Id = id,
        ProgramId = 1,
        CandidateId = candidateId,
        VoterId = voterId,
        VotedAt = votedAt ?? DateTime.UtcNow.AddHours(-1)
    };

    private static async Task<VotingDbContext> SeedChainAsync(int votes)
    {
        var context = CreateContext();
        context.VotingPrograms.Add(OwnerTestKit.Active(1, "owner-1"));
        context.Candidates.AddRange(OwnerTestKit.Candidate(1, 1, "A"), OwnerTestKit.Candidate(2, 1, "B"));
        for (int i = 1; i <= votes; i++)
            context.Votes.Add(NewVote(i, candidateId: 1, voterId: i));
        await context.SaveChangesAsync();

        var chain = Chain();
        foreach (var vote in context.Votes.OrderBy(v => v.Id).ToList())
            await chain.CreateBlockForVoteAsync(context, vote);
        return context;
    }

    // ── H01: hash covers the vote's content ─────────────────────────────────

    [Fact]
    public void H01_NewBlock_UsesVersion2AndValidatesAgainstItsVote()
    {
        var vote = NewVote(5, candidateId: 1, voterId: 9);
        var block = new BlockchainVote { VoteId = 5 };

        block.GenerateHash(vote, "0");

        Assert.Equal(BlockchainVote.CurrentHashVersion, block.HashVersion);
        Assert.True(block.IsValid(vote, "0"));
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("program")]
    [InlineData("voter")]
    [InlineData("time")]
    public void H01_ChangingAnyVoteField_InvalidatesTheBlock(string field)
    {
        var vote = NewVote(5, candidateId: 1, voterId: 9);
        var block = new BlockchainVote { VoteId = 5 };
        block.GenerateHash(vote, "0");

        switch (field)
        {
            case "candidate": vote.CandidateId = 2; break;
            case "program": vote.ProgramId = 2; break;
            case "voter": vote.VoterId = 10; break;
            case "time": vote.VotedAt = vote.VotedAt.AddSeconds(1); break;
        }

        Assert.False(block.IsValid(vote, "0"));
    }

    [Fact]
    public void H01_LegacyVersion1Block_StillValidates()
    {
        var timestamp = new DateTime(2026, 4, 18, 10, 0, 0, 123, DateTimeKind.Utc);
        var legacy = new BlockchainVote
        {
            VoteId = 5,
            HashVersion = 1,
            Timestamp = timestamp,
            PreviousHash = "0",
            Hash = BlockchainVote.ComputeHash($"5{"0"}{timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}")
        };

        Assert.True(legacy.IsValid(NewVote(5, 1, 9), "0"));
    }

    [Fact]
    public void H01_GenerateHash_WithAnotherVote_Throws()
    {
        var block = new BlockchainVote { VoteId = 5 };
        Assert.Throws<ArgumentException>(() => block.GenerateHash(NewVote(6, 1, 9), "0"));
    }

    [Fact]
    public async Task H01_TamperedCandidateInDatabase_IsDetectedByChainCheck()
    {
        using var context = await SeedChainAsync(votes: 3);
        Assert.True(await Chain().IsChainValidForProgramAsync(context, 1));

        var vote = await context.Votes.FindAsync(2);
        vote!.CandidateId = 2;   // change the ballot after the block was written
        await context.SaveChangesAsync();

        Assert.False(await Chain().IsChainValidForProgramAsync(context, 1));
    }

    [Fact]
    public async Task H01_CreateBlock_ChainsBlocksAndIsIdempotent()
    {
        using var context = await SeedChainAsync(votes: 2);
        var blocks = context.BlockchainVotes.OrderBy(b => b.Id).ToList();

        Assert.Equal("0", blocks[0].PreviousHash);
        Assert.Equal(blocks[0].Hash, blocks[1].PreviousHash);

        var again = await Chain().CreateBlockForVoteAsync(context, (await context.Votes.FindAsync(2))!);

        Assert.Equal(blocks[1].Id, again.Id);
        Assert.Equal(2, context.BlockchainVotes.Count());
    }

    // ── H02: missing / duplicate blocks ─────────────────────────────────────

    [Fact]
    public async Task H02_OldVoteWithoutBlock_MakesChainInvalid()
    {
        using var context = await SeedChainAsync(votes: 2);
        context.Votes.Add(NewVote(3, 1, 3, votedAt: DateTime.UtcNow.AddHours(-1)));
        await context.SaveChangesAsync();

        Assert.False(await Chain().IsChainValidForProgramAsync(context, 1));
    }

    [Fact]
    public async Task H02_JustCastVoteWithoutBlockYet_IsStillValid()
    {
        using var context = await SeedChainAsync(votes: 2);
        context.Votes.Add(NewVote(3, 1, 3, votedAt: DateTime.UtcNow));   // still in the queue
        await context.SaveChangesAsync();

        Assert.True(await Chain().IsChainValidForProgramAsync(context, 1));
    }

    [Fact]
    public async Task H02_DeletedNewestBlock_IsDetected()
    {
        using var context = await SeedChainAsync(votes: 3);
        context.BlockchainVotes.Remove(context.BlockchainVotes.OrderByDescending(b => b.Id).First());
        await context.SaveChangesAsync();

        Assert.False(await Chain().IsChainValidForProgramAsync(context, 1));
    }

    [Fact]
    public async Task H02_ProgramWithNoVotes_IsValid()
    {
        using var context = CreateContext();
        Assert.True(await Chain().IsChainValidForProgramAsync(context, 1));
    }

    // ── H03: sweep ──────────────────────────────────────────────────────────

    [Fact]
    public async Task H03_Sweep_QueuesOnlyVotesWithoutABlock()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var context = CreateContext(dbName))
        {
            context.Votes.AddRange(NewVote(1, 1, 1), NewVote(2, 1, 2), NewVote(3, 1, 3));
            context.BlockchainVotes.Add(new BlockchainVote { Id = 1, VoteId = 1, Hash = "x", PreviousHash = "0" });
            await context.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => CreateContext(dbName));
        var provider = services.BuildServiceProvider();

        var queued = new List<int>();
        var queue = new Mock<IVoteBlockQueue>();
        queue.Setup(q => q.TryEnqueue(It.IsAny<int>())).Callback<int>(queued.Add).Returns(true);

        var service = new BlockchainBackgroundService(
            queue.Object, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BlockchainBackgroundService>.Instance);

        var count = await service.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(new[] { 2, 3 }, queued);
    }

    // ── H04: voter linking requires a confirmed email ───────────────────────

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task H04_LinkPendingRegistrations_OnlyForConfirmedEmail(bool confirmed, int expectedLinked)
    {
        using var context = CreateContext();
        context.Voters.Add(new Voter { Id = 1, ProgramId = 1, Name = "Victim", Email = "victim@example.com" });
        await context.SaveChangesAsync();
        var user = new ApplicationUser { Id = "u1", Email = "Victim@Example.com", EmailConfirmed = confirmed };

        var linked = await VoterLinking.LinkPendingRegistrationsAsync(context, user);

        Assert.Equal(expectedLinked, linked);
        Assert.Equal(confirmed ? "u1" : null, context.Voters.Single().UserId);
    }

    [Fact]
    public async Task H04_LinkPendingRegistrations_DoesNotStealAlreadyLinkedSlot()
    {
        using var context = CreateContext();
        context.Voters.Add(new Voter { Id = 1, ProgramId = 1, Name = "V", Email = "v@example.com", UserId = "original" });
        await context.SaveChangesAsync();

        var linked = await VoterLinking.LinkPendingRegistrationsAsync(
            context, new ApplicationUser { Id = "other", Email = "v@example.com", EmailConfirmed = true });

        Assert.Equal(0, linked);
        Assert.Equal("original", context.Voters.Single().UserId);
    }

    // ── H05: admin grants ProgramOwner ──────────────────────────────────────

    private static (VotingAdminController controller, Mock<UserManager<ApplicationUser>> users) AdminWithUsers(
        VotingDbContext context, ApplicationUser user, bool isOwner)
    {
        var users = OwnerTestKit.MockUserManager();
        users.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        users.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
        users.Setup(m => m.IsInRoleAsync(user, "ProgramOwner")).ReturnsAsync(isOwner);
        users.Setup(m => m.AddToRoleAsync(user, "ProgramOwner")).ReturnsAsync(IdentityResult.Success);
        users.Setup(m => m.RemoveFromRoleAsync(user, "ProgramOwner")).ReturnsAsync(IdentityResult.Success);

        var http = new DefaultHttpContext();
        var controller = new VotingAdminController(context, Mock.Of<IBlockchainService>(), Mock.Of<IVotingService>(), users.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };
        OwnerTestKit.SetUser(controller, "admin-1", admin: true);
        return (controller, users);
    }

    [Fact]
    public async Task H05_GrantOwner_ConfirmedUser_AddsRole()
    {
        using var context = CreateContext();
        var user = new ApplicationUser { Id = "u1", Email = "a@example.com", EmailConfirmed = true };
        var (controller, users) = AdminWithUsers(context, user, isOwner: false);

        await controller.SetProgramOwner("u1", grant: true, q: null);

        users.Verify(m => m.AddToRoleAsync(user, "ProgramOwner"), Times.Once);
        Assert.True(controller.TempData.ContainsKey("Success"));
    }

    [Fact]
    public async Task H05_GrantOwner_UnconfirmedUser_IsRefused()
    {
        using var context = CreateContext();
        var user = new ApplicationUser { Id = "u1", Email = "a@example.com", EmailConfirmed = false };
        var (controller, users) = AdminWithUsers(context, user, isOwner: false);

        await controller.SetProgramOwner("u1", grant: true, q: null);

        users.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        Assert.True(controller.TempData.ContainsKey("Error"));
    }

    [Fact]
    public async Task H05_RevokeOwner_RemovesRole()
    {
        using var context = CreateContext();
        var user = new ApplicationUser { Id = "u1", Email = "a@example.com", EmailConfirmed = true };
        var (controller, users) = AdminWithUsers(context, user, isOwner: true);

        await controller.SetProgramOwner("u1", grant: false, q: null);

        users.Verify(m => m.RemoveFromRoleAsync(user, "ProgramOwner"), Times.Once);
    }

    [Fact]
    public async Task H05_SetProgramOwner_UnknownUser_ReturnsNotFound()
    {
        using var context = CreateContext();
        var (controller, _) = AdminWithUsers(context, new ApplicationUser { Id = "u1" }, isOwner: false);

        Assert.IsType<NotFoundResult>(await controller.SetProgramOwner("missing", grant: true, q: null));
    }

    [Fact]
    public async Task H05_UsersList_ShowsRolesAndFiltersBySearch()
    {
        using var context = CreateContext();
        context.Roles.Add(new IdentityRole("ProgramOwner") { Id = "r-owner" });
        context.Users.AddRange(
            new ApplicationUser { Id = "u1", Email = "owner@example.com", FullName = "Owner One", EmailConfirmed = true },
            new ApplicationUser { Id = "u2", Email = "voter@example.com", FullName = "Voter Two" });
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = "u1", RoleId = "r-owner" });
        await context.SaveChangesAsync();
        var (controller, _) = AdminWithUsers(context, new ApplicationUser { Id = "x" }, isOwner: false);

        var all = Assert.IsAssignableFrom<IEnumerable<VotingAdminController.UserRow>>(
            Assert.IsType<ViewResult>(await controller.Users(null)).Model).ToList();
        var filtered = Assert.IsAssignableFrom<IEnumerable<VotingAdminController.UserRow>>(
            Assert.IsType<ViewResult>(await controller.Users("voter")).Model).ToList();

        Assert.True(all.Single(u => u.Id == "u1").IsProgramOwner);
        Assert.False(all.Single(u => u.Id == "u2").IsProgramOwner);
        Assert.Equal("u2", Assert.Single(filtered).Id);
    }

    // ── H06: voter home page ────────────────────────────────────────────────

    private static HomeController Home(VotingDbContext context, string userId, string? role = null)
    {
        var users = OwnerTestKit.MockUserManager();
        users.Setup(m => m.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(userId);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new HomeController(context, users.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
            }
        };
    }

    [Fact]
    public async Task H06_SignedInVoter_SeesOnlyTheirPublishedPrograms()
    {
        using var context = CreateContext();
        context.VotingPrograms.AddRange(OwnerTestKit.Active(1, "o"), OwnerTestKit.Upcoming(2, "o"), OwnerTestKit.Ended(3, "o"));
        context.Voters.AddRange(
            new Voter { Id = 1, ProgramId = 1, Name = "Me", UserId = "voter-1" },
            new Voter { Id = 2, ProgramId = 2, Name = "Me", UserId = "voter-1" },   // draft: hidden
            new Voter { Id = 3, ProgramId = 3, Name = "Someone", UserId = "voter-2" });
        await context.SaveChangesAsync();

        var view = Assert.IsType<ViewResult>(await Home(context, "voter-1").Index());

        Assert.Equal("MyElections", view.ViewName);
        var program = Assert.Single(Assert.IsAssignableFrom<IEnumerable<Voter>>(view.Model));
        Assert.Equal(1, program.ProgramId);
    }

    [Fact]
    public async Task H06_ProgramOwner_IsRedirectedToMyPrograms()
    {
        using var context = CreateContext();
        var redirect = Assert.IsType<RedirectToActionResult>(await Home(context, "o1", "ProgramOwner").Index());
        Assert.Equal("MyPrograms", redirect.ActionName);
    }

    // ── H07: security headers ───────────────────────────────────────────────

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    public void H07_SecurityHeaders_AreSet(string header, string expected)
    {
        var headers = new HeaderDictionary();
        SecurityHeaders.Apply(headers);
        Assert.Equal(expected, headers[header].ToString());
    }

    [Theory]
    [InlineData("frame-ancestors 'none'")]
    [InlineData("object-src 'none'")]
    [InlineData("https://cdn.jsdelivr.net")]
    public void H07_ContentSecurityPolicy_ContainsRequiredDirectives(string directive)
    {
        var headers = new HeaderDictionary();
        SecurityHeaders.Apply(headers);
        Assert.Contains(directive, headers["Content-Security-Policy"].ToString());
    }

    // ── H08: email tokens ───────────────────────────────────────────────────

    [Fact]
    public void H08_Token_RoundTripsThroughUrlSafeEncoding()
    {
        const string token = "CfDJ8+/abc==xyz";
        var encoded = AccountEmails.EncodeToken(token);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.Equal(token, AccountEmails.DecodeToken(encoded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    public void H08_InvalidToken_DecodesToNull(string? code)
    {
        Assert.Null(AccountEmails.DecodeToken(code));
    }
}
