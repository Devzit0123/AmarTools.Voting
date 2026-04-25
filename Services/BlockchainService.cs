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
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ILogger<BlockchainService> _logger;

        public BlockchainService(ILogger<BlockchainService> logger)
        {
            _logger = logger;

            // Retry policy: 3 attempts with exponential backoff
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2s, 4s, 8s
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        // Use structured logging
                        _logger.LogWarning(exception, "Blockchain retry {RetryCount} after {DelaySeconds}s due to: {Message}", retryCount, timeSpan.TotalSeconds, exception.Message);
                    });
        }

        /// <summary>
        /// Verifies the integrity of the blockchain for a given program.
        /// </summary>
        public async Task<bool> IsChainValidForProgramAsync(VotingDbContext context, int programId)
        {
            var blocks = await context.BlockchainVotes
                .Include(b => b.Vote)
                .Where(b => b.Vote.ProgramId == programId)
                .OrderBy(b => b.Id)
                .AsNoTracking()
                .ToListAsync();

            if (blocks.Count == 0) return true;

            string previousHash = "0";

            foreach (var block in blocks)
            {
                string recomputed = BlockchainVote.ComputeHash(
                    $"{block.VoteId}{previousHash}{block.Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}");

                if (block.Hash != recomputed || block.PreviousHash != previousHash)
                    return false;

                previousHash = block.Hash;
            }

            return true;
        }

        /// <summary>
        /// Creates a blockchain block for a vote with retry logic.
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
                    // Get the last block's hash for this program
                    string previousHash = await context.BlockchainVotes
                        .Include(b => b.Vote)
                        .Where(b => b.Vote.ProgramId == vote.ProgramId)
                        .OrderByDescending(b => b.Id)
                        .Select(b => b.Hash)
                        .FirstOrDefaultAsync() ?? "0";

                    var block = new BlockchainVote { VoteId = vote.Id };
                    block.GenerateHash(previousHash);

                    context.BlockchainVotes.Add(block);
                    await context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return block;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
    }
}