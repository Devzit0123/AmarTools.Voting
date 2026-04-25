using Microsoft.EntityFrameworkCore;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;

namespace AmarTools.Voting.Services
{
    public interface IBlockchainService
    {
        Task<bool> IsChainValidForProgramAsync(VotingDbContext context, int programId);
        Task<BlockchainVote> CreateBlockForVoteAsync(VotingDbContext context, Vote vote);
    }
}