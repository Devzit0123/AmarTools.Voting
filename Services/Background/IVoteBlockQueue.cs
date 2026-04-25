using System.Threading;
using System.Threading.Tasks;

namespace AmarTools.Voting.Services.Background
{
    public interface IVoteBlockQueue
    {
        ValueTask EnqueueAsync(int voteId);
        ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
    }
}
