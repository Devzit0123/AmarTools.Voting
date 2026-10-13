using System.Security.Claims;
using AmarTools.Voting.Controllers;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using AmarTools.Voting.Services.Background;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  Shared test kit for VotingController tests.
//
//  WHY SQLite (and not EF InMemory)?
//  VotingController.CastVote calls Database.BeginTransactionAsync(IsolationLevel.Serializable).
//  That is a *relational* API: on the EF InMemory provider it throws, so the success path of
//  CastVote could never be reached. SQLite in-memory is relational, supports transactions and
//  enforces the real unique indexes (Votes: VoterId+ProgramId), so every path can be tested.
//  Foreign keys are switched OFF so tests do not need to seed owner/user rows.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A DbContext that can be told to fail on SaveChangesAsync (failure-path tests).</summary>
public sealed class VotingTestDbContext : VotingDbContext
{
    public Exception? FailWith { get; set; }

    public VotingTestDbContext(DbContextOptions<VotingDbContext> options) : base(options) { }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (FailWith is not null) throw FailWith;
        return base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>One controller + one private in-memory SQLite database per test.</summary>
public sealed class VotingHarness : IDisposable
{
    public const string DefaultUserId = "voter-1";
    public const int ProgramId = 1;
    public const int CandidateA = 10;   // program 1, "Alice"
    public const int CandidateB = 11;   // program 1, "Bob"

    private readonly SqliteConnection _connection;

    public VotingTestDbContext Db { get; }
    public Mock<IVotingService> Service { get; } = new();
    public Mock<IBlockchainService> Blockchain { get; } = new();
    public Mock<IVoteBlockQueue> Queue { get; } = new();
    public VotingController Controller { get; }

    public string? Error => Controller.TempData["Error"] as string;
    public string? Success => Controller.TempData["Success"] as string;
    public string? Warning => Controller.TempData["Warning"] as string;

    /// <param name="userId">NameIdentifier claim value; null = signed in but WITHOUT the claim.</param>
    /// <param name="authenticated">false = anonymous visitor.</param>
    public VotingHarness(string? userId = DefaultUserId, bool authenticated = true)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = OFF;";
            pragma.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<VotingDbContext>()
            .UseSqlite(_connection)
            .Options;
        Db = new VotingTestDbContext(options);
        Db.Database.EnsureCreated();

        Queue.Setup(q => q.TryEnqueue(It.IsAny<int>())).Returns(true);
        Blockchain.Setup(b => b.IsChainValidForProgramAsync(It.IsAny<VotingDbContext>(), It.IsAny<int>()))
                  .ReturnsAsync(true);
        Service.Setup(s => s.GetResultsAsync(It.IsAny<int>()))
               .ReturnsAsync(new List<CandidateResultViewModel>());

        Controller = new VotingController(Db, Service.Object, Blockchain.Object, Queue.Object);

        var http = new DefaultHttpContext { User = BuildUser(userId, authenticated) };
        Controller.ControllerContext = new ControllerContext { HttpContext = http };
        Controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
    }

    public static ClaimsPrincipal BuildUser(string? userId, bool authenticated)
    {
        if (!authenticated)
            return new ClaimsPrincipal(new ClaimsIdentity());   // IsAuthenticated == false

        var claims = new List<Claim>();
        if (userId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    /// <summary>
    /// Saves a program (+ candidates id*10 and id*10+1, e.g. 10 and 11 for program 1) and makes the
    /// mocked IVotingService return it. Offsets are seconds relative to "now".
    /// </summary>
    public VotingProgram AddProgram(
        int id = ProgramId,
        bool published = true,
        double startOffsetSeconds = -3600,
        double endOffsetSeconds = 3600,
        string? name = null,
        int candidateCount = 2)
    {
        var program = new VotingProgram
        {
            Id = id,
            ProgramName = name ?? $"Test Program {id}",
            OwnerId = "owner-1",
            IsPublished = published,
            StartTime = DateTime.UtcNow.AddSeconds(startOffsetSeconds),
            EndTime = DateTime.UtcNow.AddSeconds(endOffsetSeconds),
        };
        Db.VotingPrograms.Add(program);

        var names = new[] { "Alice", "Bob", "Carol", "Dave" };
        var codes = new[] { "A", "B", "C", "D" };
        for (int i = 0; i < candidateCount && i < names.Length; i++)
        {
            Db.Candidates.Add(new Candidate
            {
                Id = id * 10 + i,
                ProgramId = id,
                Name = names[i],
                CandidateCode = codes[i],
            });
        }
        Db.SaveChanges();

        // The real service returns a detached copy (AsNoTracking + Include).
        Service.Setup(s => s.GetProgramWithCandidatesAsync(id))
               .ReturnsAsync(() => Db.VotingPrograms.AsNoTracking()
                                      .Include(p => p.Candidates)
                                      .First(p => p.Id == id));
        return program;
    }

    public Voter AddVoter(
        string? userId = DefaultUserId,
        int programId = ProgramId,
        bool hasVoted = false,
        string name = "Test Voter")
    {
        var voter = new Voter
        {
            Name = name,
            UserId = userId,
            ProgramId = programId,
            HasVoted = hasVoted,
            RegisteredAt = DateTime.UtcNow,
            RegistrationSource = "owner",
        };
        Db.Voters.Add(voter);
        Db.SaveChanges();
        return voter;
    }

    public ApplicationUser AddUser(string id, string userName, string fullName = "", string? email = null)
    {
        var user = new ApplicationUser { Id = id, UserName = userName, Email = email, FullName = fullName };
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    /// <summary>Adds an already-existing vote row (used to provoke the unique index).</summary>
    public Vote AddVote(int voterId, int candidateId = CandidateA, int programId = ProgramId)
    {
        var vote = new Vote
        {
            ProgramId = programId,
            CandidateId = candidateId,
            VoterId = voterId,
            VotedAt = DateTime.UtcNow,
        };
        Db.Votes.Add(vote);
        Db.SaveChanges();
        return vote;
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
