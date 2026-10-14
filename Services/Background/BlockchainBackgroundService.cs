using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using AmarTools.Voting.Data;
using Microsoft.EntityFrameworkCore;

namespace AmarTools.Voting.Services.Background
{
    /// <summary>
    /// Creates blockchain blocks for queued votes. A sweep runs at startup and then every
    /// <see cref="SweepInterval"/> to queue votes that have no block, so votes queued just
    /// before a restart or dropped because the queue was full still get one (D20).
    /// </summary>
    public class BlockchainBackgroundService : BackgroundService
    {
        public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(2);
        private const int SweepBatchSize = 500;

        private readonly IVoteBlockQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BlockchainBackgroundService> _logger;

        public BlockchainBackgroundService(
            IVoteBlockQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<BlockchainBackgroundService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Blockchain background service started.");
            await Task.WhenAll(ProcessQueueAsync(stoppingToken), SweepLoopAsync(stoppingToken));
            _logger.LogInformation("Blockchain background service is stopping.");
        }

        private async Task ProcessQueueAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int voteId = await _queue.DequeueAsync(stoppingToken);

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();

                        var db = scope.ServiceProvider.GetRequiredService<VotingDbContext>();
                        var blockchainService = scope.ServiceProvider.GetRequiredService<IBlockchainService>();

                        var vote = await db.Votes.FindAsync(new object[] { voteId }, stoppingToken);

                        if (vote == null)
                        {
                            _logger.LogWarning("Vote {VoteId} not found.", voteId);
                            continue;
                        }

                        _logger.LogInformation(
                            "Processing blockchain block for Vote {VoteId} (Program {ProgramId}).",
                            voteId, vote.ProgramId);

                        await blockchainService.CreateBlockForVoteAsync(db, vote);

                        _logger.LogInformation("Blockchain block created for Vote {VoteId}.", voteId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Error processing Vote {VoteId}. The sweep will retry it.", voteId);
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // graceful shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in background loop.");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private async Task SweepLoopAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                    await Task.Delay(SweepInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Blockchain sweep failed; retrying in {Interval}.", SweepInterval);
                    try { await Task.Delay(SweepInterval, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>Queues up to <see cref="SweepBatchSize"/> votes that have no block. Returns how many were queued.</summary>
        internal async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VotingDbContext>();

            var missing = await db.Votes
                .Where(v => !db.BlockchainVotes.Any(b => b.VoteId == v.Id))
                .OrderBy(v => v.Id)
                .Select(v => v.Id)
                .Take(SweepBatchSize)
                .ToListAsync(cancellationToken);

            int queued = 0;
            foreach (var voteId in missing)
            {
                if (!_queue.TryEnqueue(voteId)) break; // queue full: the next sweep continues
                queued++;
            }

            if (queued > 0)
                _logger.LogInformation("Blockchain sweep queued {Count} vote(s) that had no block.", queued);

            return queued;
        }
    }
}
