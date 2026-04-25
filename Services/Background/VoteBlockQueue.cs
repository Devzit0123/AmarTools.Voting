using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;

namespace AmarTools.Voting.Services.Background
{
    public class VoteBlockQueue : IVoteBlockQueue
    {
        private readonly Channel<int> _queue;

        public VoteBlockQueue(int capacity = 1000)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<int>(options);
        }

        public async ValueTask EnqueueAsync(int voteId)
        {
            await _queue.Writer.WriteAsync(voteId);
        }

        public async ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
        {
            var item = await _queue.Reader.ReadAsync(cancellationToken);
            return item;
        }
    }
}
