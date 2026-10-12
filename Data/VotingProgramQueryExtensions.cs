using AmarTools.Voting.Models;
using Microsoft.EntityFrameworkCore;

namespace AmarTools.Voting.Data
{
    public static class VotingProgramQueryExtensions
    {
        /// <summary>
        /// Loads the programs with candidate, vote and voter counts computed by the database
        /// instead of loading those collections (list pages only show the numbers).
        /// </summary>
        public static async Task<List<VotingProgram>> ToListWithCountsAsync(this IQueryable<VotingProgram> query)
        {
            var rows = await query
                .Select(p => new
                {
                    Program = p,
                    Candidates = p.Candidates.Count,
                    Votes = p.Votes.Count,
                    Voters = p.Voters.Count
                })
                .ToListAsync();

            foreach (var row in rows)
            {
                row.Program.LoadedCandidateCount = row.Candidates;
                row.Program.LoadedVoteCount = row.Votes;
                row.Program.LoadedVoterCount = row.Voters;
            }

            return rows.Select(r => r.Program).ToList();
        }
    }
}
