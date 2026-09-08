using AmarTools.Voting.Data;
using Microsoft.EntityFrameworkCore;

namespace AmarTools.Voting.UnitTests.TestHelpers
{
    /// <summary>
    /// Creates a fresh, isolated in-memory VotingDbContext per test so tests never
    /// share state and never require a real PostgreSQL instance.
    /// </summary>
    public static class VotingDbContextFactory
    {
        public static VotingDbContext Create(string? dbName = null)
        {
            var options = new DbContextOptionsBuilder<VotingDbContext>()
                .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
                .Options;

            return new VotingDbContext(options);
        }
    }
}
