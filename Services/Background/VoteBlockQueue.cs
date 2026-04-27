using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AmarTools.Voting.Services.Background
{
    public class VoteBlockQueue : IVoteBlockQueue
    {
        private readonly Channel<int>           _queue;
        private readonly ILogger<VoteBlockQueue> _logger;

        public VoteBlockQueue(ILogger<VoteBlockQueue> logger, int capacity = 1000)
        {
            _logger = logger;

            
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode      = BoundedChannelFullMode.DropWrite,
                SingleReader  = true,   
                SingleWriter  = false,  
            };

            _queue = Channel.CreateBounded<int>(options);
        }

        /// <inheritdoc/>
        public bool TryEnqueue(int voteId)
        {
            bool written = _queue.Writer.TryWrite(voteId);

            if (!written)
            {
                
                _logger.LogWarning(
                    "Blockchain queue is full (capacity reached). " +
                    "Vote {VoteId} was not enqueued. " +
                    "The blockchain block will be missing until a sweep is run.",
                    voteId);
            }

            return written;
        }

        /// <inheritdoc/>
        public async ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
        {
            return await _queue.Reader.ReadAsync(cancellationToken);
        }
    }
}
