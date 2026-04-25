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
    public class BlockchainBackgroundService : BackgroundService
    {
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

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int voteId = await _queue.DequeueAsync(stoppingToken);

                    try
                    {
                        // ✅ Create scope (VERY IMPORTANT)
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
                            voteId, vote.ProgramId
                        );

                        await blockchainService.CreateBlockForVoteAsync(db, vote);

                        _logger.LogInformation("Blockchain block created for Vote {VoteId}.", voteId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Vote {VoteId}.", voteId);
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

            _logger.LogInformation("Blockchain background service is stopping.");
        }
    }
}