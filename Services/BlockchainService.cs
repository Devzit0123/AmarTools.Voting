using Microsoft.EntityFrameworkCore;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System;
using System.Threading.Tasks;

namespace AmarTools.Voting.Services
{
    public class BlockchainService : IBlockchainService
    {
        /// <summary>
        /// A vote this old without a block counts as a broken chain (D19). Younger votes
        /// may still be waiting in the background queue.
        /// </summary>
        public static readonly TimeSpan MissingBlockGracePeriod = TimeSpan.FromMinutes(5);

        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ILogger<BlockchainService> _logger;

        public BlockchainService(ILogger<BlockchainService> logger)
        {
            _logger = logger;

            // 3 retries: 0.5 s, 1 s, 2 s. The queue has a single reader, so long delays
            // would hold up every vote behind a failing one (D21).
            _retryPolicy = Policy
                .Handle<Exception>(ex => ex is not ArgumentException)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(exception, "Blockchain retry {RetryCount} after {DelaySeconds}s due to: {Message}", retryCount, timeSpan.TotalSeconds, exception.Message);
                    });
        }

        /// <summary>
        /// Verifies the program's chain: every block links to the previous one, every
        /// block's hash matches its vote's current content (D18), and no vote older than
        /// <see cref="MissingBlockGracePeriod"/> is missing its block (D19).
        /// </summary>
        public async Task<bool> IsChainValidForProgramAsync(VotingDbContext context, int programId)
        {
            var blocks = await context.BlockchainVotes
                .Include(b => b.Vote)
                .Where(b => b.Vote.ProgramId == programId)
                .OrderBy(b => b.Id)
                .AsNoTracking()
                .ToListAsync();

            string previousHash = "0";
            foreach (var block in blocks)
            {
                if (!block.IsValid(block.Vote, previousHash))
                {
                    _logger.LogWarning("Blockchain check failed for program {ProgramId} at block {BlockId} (vote {VoteId}).",
                        programId, block.Id, block.VoteId);
                    return false;
                }
                previousHash = block.Hash;
            }

            // Two blocks for one vote means the chain was tampered with or replayed.
            if (blocks.Select(b => b.VoteId).Distinct().Count() != blocks.Count)
                return false;

            var cutoff = DateTime.UtcNow - MissingBlockGracePeriod;
            bool votesWithoutBlock = await context.Votes
                .Where(v => v.ProgramId == programId && v.VotedAt < cutoff)
                .AnyAsync(v => !context.BlockchainVotes.Any(b => b.VoteId == v.Id));
            if (votesWithoutBlock)
            {
                _logger.LogWarning("Program {ProgramId} has votes older than {Minutes} minutes without a blockchain block.",
                    programId, MissingBlockGracePeriod.TotalMinutes);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates the block for a vote, with retries. If the vote already has a block
        /// (for example it was queued by both CastVote and the sweep), that block is
        /// returned instead of adding a second one.
        /// </summary>
        public async Task<BlockchainVote> CreateBlockForVoteAsync(VotingDbContext context, Vote vote)
        {
            if (vote == null || vote.Id == 0)
                throw new ArgumentException("Vote must be saved with a valid Id before creating a block.");

            return await _retryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database
                    .BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                try
                {
                    var existing = await context.BlockchainVotes.FirstOrDefaultAsync(b => b.VoteId == vote.Id);
                    if (existing is not null)
                    {
                        await transaction.CommitAsync();
                        return existing;
                    }

                    string previousHash = await context.BlockchainVotes
                        .Where(b => b.Vote.ProgramId == vote.ProgramId)
                        .OrderByDescending(b => b.Id)
                        .Select(b => b.Hash)
                        .FirstOrDefaultAsync() ?? "0";

                    var block = new BlockchainVote { VoteId = vote.Id };
                    block.GenerateHash(vote, previousHash);

                    context.BlockchainVotes.Add(block);
                    await context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return block;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    // A failed SaveChanges leaves the block tracked; detach it so the retry starts clean.
                    foreach (var entry in context.ChangeTracker.Entries<BlockchainVote>()
                                 .Where(e => e.State == EntityState.Added).ToList())
                        entry.State = EntityState.Detached;
                    throw;
                }
            });
        }
    }
}
