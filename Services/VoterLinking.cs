using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using Microsoft.EntityFrameworkCore;

namespace AmarTools.Voting.Services
{
    /// <summary>
    /// Links voter rows that an owner pre-registered by email to the account that owns
    /// that email. Call it only for accounts whose email is confirmed (D15): linking an
    /// unconfirmed account would let anyone claim a voter slot by signing up with
    /// someone else's address.
    /// </summary>
    public static class VoterLinking
    {
        public static async Task<int> LinkPendingRegistrationsAsync(VotingDbContext context, ApplicationUser user)
        {
            if (!user.EmailConfirmed) return 0;

            var normalizedEmail = user.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail)) return 0;

            var pendingVoters = await context.Voters
                .Where(v => v.Email != null && v.Email.ToLower() == normalizedEmail && v.UserId == null)
                .ToListAsync();

            foreach (var voter in pendingVoters)
                voter.UserId = user.Id;

            if (pendingVoters.Count > 0)
                await context.SaveChangesAsync();

            return pendingVoters.Count;
        }
    }
}
