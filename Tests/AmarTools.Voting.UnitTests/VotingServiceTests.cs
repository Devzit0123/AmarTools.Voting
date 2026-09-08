using System.Security.Claims;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using AmarTools.Voting.UnitTests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AmarTools.Voting.UnitTests
{
    /// <summary>
    /// Covers the M4 decision table (RegisterVoterByEmailAsync) and the M4 basis-path
    /// methods (ValidateProgramAsync via ValidateAndCreateProgramAsync, RemoveVoterAsync,
    /// GetResultsAsync). See ECT_DecisionTable and BasisPath tabs for the full design.
    /// </summary>
    public class VotingServiceTests
    {
        private static Mock<UserManager<ApplicationUser>> MockUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }

        private static IHttpContextAccessor AuthenticatedHttpContext(string userId, string name, bool isAdmin = false)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(ClaimTypes.Name, name),
            };
            if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));

            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext { User = principal };

            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(httpContext);
            return accessor.Object;
        }

        private static IHttpContextAccessor UnauthenticatedHttpContext()
        {
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());
            return accessor.Object;
        }

        private static VotingService BuildService(VotingDbContext ctx, IHttpContextAccessor accessor, Mock<UserManager<ApplicationUser>>? um = null)
        {
            um ??= MockUserManager();
            return new VotingService(ctx, accessor, um.Object, NullLogger<VotingService>.Instance);
        }

        // ── ValidateAndCreateProgramAsync — Basis Path P1..P6 ────────────────────────

        [Fact] // Path 1
        public async Task Create_NotAuthenticated_Fails()
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, UnauthenticatedHttpContext());

            var (success, error) = await service.ValidateAndCreateProgramAsync(new VotingProgram
            {
                ProgramName = "Valid Election",
                StartTime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(2),
            });

            Assert.False(success);
            Assert.Contains("logged in", error);
        }

        [Fact] // Path 2
        public async Task Create_BlankProgramName_Fails()
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, AuthenticatedHttpContext("user-1", "Owner One"));

            var (success, error) = await service.ValidateAndCreateProgramAsync(new VotingProgram
            {
                ProgramName = "   ",
                StartTime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(2),
            });

            Assert.False(success);
            Assert.Contains("Program name is required", error);
        }

        [Fact] // Path 3
        public async Task Create_DefaultDateTimes_Fails()
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, AuthenticatedHttpContext("user-1", "Owner One"));

            var (success, error) = await service.ValidateAndCreateProgramAsync(new VotingProgram
            {
                ProgramName = "Election With Default Dates",
                StartTime = default,
                EndTime = default,
            });

            Assert.False(success);
            Assert.Contains("valid start and end times", error);
        }

        [Theory] // Path 4 — BVA on the 5-minute boundary (paired with BVA tab rows 19-24)
        [InlineData(4, 59, false)] // Min-1
        [InlineData(5, 0, true)]   // Min (inclusive)
        [InlineData(5, 1, true)]   // Min+1
        public async Task Create_DurationBoundary_AroundFiveMinutes(int minutes, int seconds, bool expectedSuccess)
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, AuthenticatedHttpContext("user-1", "Owner One"));
            var start = DateTime.UtcNow.AddHours(1);

            var (success, error) = await service.ValidateAndCreateProgramAsync(new VotingProgram
            {
                ProgramName = "Duration Boundary Election",
                StartTime = start,
                EndTime = start.AddMinutes(minutes).AddSeconds(seconds),
            });

            Assert.Equal(expectedSuccess, success);
            if (!expectedSuccess) Assert.Contains("at least 5 minutes", error);
        }

        [Fact] // Path 5
        public async Task Create_DuplicateSlug_CaseInsensitive_Fails()
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, AuthenticatedHttpContext("user-1", "Owner One"));

            await service.ValidateAndCreateProgramAsync(new VotingProgram
            {
                ProgramName = "First Election",
                StartTime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(2),
                Slug = "Election-2026",
            });

            var (success, error) = await service.ValidateAndCreateProgramAsync(new VotingProgram
            {
                ProgramName = "Second Election",
                StartTime = DateTime.UtcNow.AddHours(3),
                EndTime = DateTime.UtcNow.AddHours(4),
                Slug = "election-2026", // same slug, different casing
            });

            Assert.False(success);
            Assert.Contains("already taken", error);
        }

        [Fact] // Path 6 — happy path
        public async Task Create_ValidProgram_Succeeds_AndNormalizesToUtc()
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, AuthenticatedHttpContext("user-1", "Owner One"));

            var model = new VotingProgram
            {
                ProgramName = "Valid Student Council Election",
                StartTime = DateTime.SpecifyKind(DateTime.Now.AddDays(1), DateTimeKind.Local),
                EndTime = DateTime.SpecifyKind(DateTime.Now.AddDays(1).AddHours(2), DateTimeKind.Local),
                Slug = "  Student Council!! ",
            };

            var (success, error) = await service.ValidateAndCreateProgramAsync(model);

            Assert.True(success);
            Assert.Null(error);
            Assert.Equal(DateTimeKind.Utc, model.StartTime.Kind);
            Assert.Equal("student-council", model.Slug);
        }

        // ── RegisterVoterByEmailAsync — Decision Table R1..R5 ────────────────────────

        [Fact] // R1
        public async Task RegisterVoter_BlankNameOrEmail_Fails()
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, UnauthenticatedHttpContext());

            var (success, error) = await service.RegisterVoterByEmailAsync(1, "", "", null, "admin");

            Assert.False(success);
            Assert.Equal("Name and Email are required.", error);
        }

        [Fact] // R2
        public async Task RegisterVoter_ProgramNotFound_Fails()
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, UnauthenticatedHttpContext());

            var (success, error) = await service.RegisterVoterByEmailAsync(9999, "Jane Voter", "jane@test.local", null, "admin");

            Assert.False(success);
            Assert.Equal("Voting program not found.", error);
        }

        [Fact] // R3
        public async Task RegisterVoter_DuplicateEmailInSameProgram_Fails()
        {
            using var ctx = VotingDbContextFactory.Create();
            var program = new VotingProgram { ProgramName = "Election", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1) };
            ctx.VotingPrograms.Add(program);
            await ctx.SaveChangesAsync();
            ctx.Voters.Add(new Voter { Name = "Existing Voter", Email = "dup@test.local", ProgramId = program.Id, RegisteredAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx, UnauthenticatedHttpContext());
            var (success, error) = await service.RegisterVoterByEmailAsync(program.Id, "New Voter", "DUP@test.local", null, "admin");

            Assert.False(success);
            Assert.Equal("This email is already registered for this program.", error);
        }

        [Fact] // R5 — happy path
        public async Task RegisterVoter_NewUniqueVoter_Succeeds()
        {
            using var ctx = VotingDbContextFactory.Create();
            var program = new VotingProgram { ProgramName = "Election", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1) };
            ctx.VotingPrograms.Add(program);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx, UnauthenticatedHttpContext());
            var (success, error) = await service.RegisterVoterByEmailAsync(program.Id, "Brand New Voter", "new@test.local", "STU-001", "admin");

            Assert.True(success);
            Assert.Null(error);
            Assert.Single(ctx.Voters, v => v.Email == "new@test.local" && v.MemberId == "STU-001");
        }

        [Fact]
        // Regression test documenting BUG-004: invalid email format is NOT rejected
        // by the service layer, unlike model-bound Voter.Email ([EmailAddress]).
        public async Task RegisterVoter_MalformedEmail_IsCurrentlyAccepted_BUG004()
        {
            using var ctx = VotingDbContextFactory.Create();
            var program = new VotingProgram { ProgramName = "Election", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1) };
            ctx.VotingPrograms.Add(program);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx, UnauthenticatedHttpContext());
            var (success, _) = await service.RegisterVoterByEmailAsync(program.Id, "Test Voter", "not-an-email", null, "admin");

            // This currently passes, demonstrating the defect. Once BUG-004 is fixed,
            // flip this assertion to Assert.False(success).
            Assert.True(success);
        }

        // ── RemoveVoterAsync — Basis Path P1..P3 ─────────────────────────────────────

        [Fact] // Path 2 — voter belongs to a different program
        public async Task RemoveVoter_WrongProgramId_ReturnsNotFound_AndDoesNotRemove()
        {
            using var ctx = VotingDbContextFactory.Create();
            var programA = new VotingProgram { ProgramName = "Program A", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1) };
            var programB = new VotingProgram { ProgramName = "Program B", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1) };
            ctx.VotingPrograms.AddRange(programA, programB);
            await ctx.SaveChangesAsync();
            var voter = new Voter { Name = "Voter In A", ProgramId = programA.Id, RegisteredAt = DateTime.UtcNow };
            ctx.Voters.Add(voter);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx, UnauthenticatedHttpContext());
            var (success, error) = await service.RemoveVoterAsync(voter.Id, programB.Id);

            Assert.False(success);
            Assert.Equal("Voter not found.", error);
            Assert.Equal(1, ctx.Voters.Count(v => v.Id == voter.Id)); // still present, not removed
        }

        [Fact] // Path 3 — happy path
        public async Task RemoveVoter_ValidVoter_Succeeds()
        {
            using var ctx = VotingDbContextFactory.Create();
            var program = new VotingProgram { ProgramName = "Program", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1) };
            ctx.VotingPrograms.Add(program);
            await ctx.SaveChangesAsync();
            var voter = new Voter { Name = "Voter", ProgramId = program.Id, RegisteredAt = DateTime.UtcNow };
            ctx.Voters.Add(voter);
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx, UnauthenticatedHttpContext());
            var (success, error) = await service.RemoveVoterAsync(voter.Id, program.Id);

            Assert.True(success);
            Assert.Null(error);
            Assert.Empty(ctx.Voters);
        }

        // ── GetResultsAsync — Basis Path P1..P3 ──────────────────────────────────────

        [Fact] // Path 1
        public async Task GetResults_ProgramNotFound_ReturnsEmptyList()
        {
            using var ctx = VotingDbContextFactory.Create();
            var service = BuildService(ctx, UnauthenticatedHttpContext());

            var results = await service.GetResultsAsync(9999);

            Assert.Empty(results);
        }

        [Fact] // Path 2 + Path 3
        public async Task GetResults_OrdersCandidatesByVoteCountDescending_ZeroVotesDefaultsToZero()
        {
            using var ctx = VotingDbContextFactory.Create();
            var program = new VotingProgram { ProgramName = "Election", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1) };
            ctx.VotingPrograms.Add(program);
            await ctx.SaveChangesAsync();

            var low = new Candidate { Name = "Low Votes", CandidateCode = "C1", ProgramId = program.Id };
            var high = new Candidate { Name = "High Votes", CandidateCode = "C2", ProgramId = program.Id };
            var zero = new Candidate { Name = "Zero Votes", CandidateCode = "C3", ProgramId = program.Id };
            ctx.Candidates.AddRange(low, high, zero);
            await ctx.SaveChangesAsync();

            var voter1 = new Voter { Name = "V1", ProgramId = program.Id, RegisteredAt = DateTime.UtcNow };
            var voter2 = new Voter { Name = "V2", ProgramId = program.Id, RegisteredAt = DateTime.UtcNow };
            ctx.Voters.AddRange(voter1, voter2);
            await ctx.SaveChangesAsync();

            ctx.Votes.Add(new Vote { ProgramId = program.Id, CandidateId = high.Id, VoterId = voter1.Id });
            ctx.Votes.Add(new Vote { ProgramId = program.Id, CandidateId = high.Id, VoterId = voter2.Id });
            await ctx.SaveChangesAsync();

            var service = BuildService(ctx, UnauthenticatedHttpContext());
            var results = await service.GetResultsAsync(program.Id);

            Assert.Equal(3, results.Count);
            Assert.Equal("High Votes", results[0].Candidate.Name);
            Assert.Equal(2, results[0].VoteCount);
            Assert.Equal(0, results.Single(r => r.Candidate.Name == "Zero Votes").VoteCount);
        }
    }
}
