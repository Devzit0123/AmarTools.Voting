using System.Security.Claims;
using System.Text.RegularExpressions;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AmarTools.Voting.Services
{
    public class VotingService(
        VotingDbContext context,
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        ILogger<VotingService> logger) : IVotingService
    {
        private static readonly TimeSpan MinimumProgramDuration = TimeSpan.FromMinutes(5);
        private static readonly Regex InvalidSlugCharacters = new("[^a-z0-9_-]+", RegexOptions.Compiled);

        // ── Programs ──────────────────────────────────────────────────────────

        public async Task<List<VotingProgram>> GetAllProgramsForDashboardAsync()
        {
            return await context.VotingPrograms
                .AsNoTracking()
                .Include(p => p.Candidates)
                .Include(p => p.Votes)
                .Include(p => p.Voters)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<VotingProgram?> GetProgramWithCandidatesAsync(int programId)
        {
            return await context.VotingPrograms
                .Include(p => p.Candidates)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == programId);
        }

        public async Task<bool> ProgramExistsAsync(int id)
        {
            return await context.VotingPrograms.AnyAsync(e => e.Id == id);
        }

        public async Task<(bool success, string? errorMessage)> ValidateAndCreateProgramAsync(VotingProgram model)
        {
            var (isValid, errorMessage, currentUser) = await ValidateProgramAsync(model);
            if (!isValid || currentUser is null)
                return (false, errorMessage);

            model.OwnerId   = currentUser.UserId;
            model.CreatedBy = currentUser.DisplayName;
            model.CreatedAt = DateTime.UtcNow;

            try
            {
                context.VotingPrograms.Add(model);
                await context.SaveChangesAsync();
                logger.LogInformation("Voting program created. ID: {ProgramId}, Owner: {UserId}",
                    model.Id, currentUser.UserId);
                return (true, null);
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Database error while creating voting program for user {UserId}.", currentUser.UserId);
                return (false, "The voting program could not be saved. Please try again.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while creating voting program.");
                return (false, "An unexpected error occurred. Please try again.");
            }
        }

        public async Task<(bool success, string? errorMessage)> ValidateAndUpdateProgramAsync(int id, VotingProgram model)
        {
            var existingProgram = await context.VotingPrograms.FindAsync(id);
            if (existingProgram is null)
                return (false, "Voting program not found.");

            var (isValid, errorMessage, currentUser) = await ValidateProgramAsync(model, existingProgram.Id);
            if (!isValid || currentUser is null)
                return (false, errorMessage);

            if (!currentUser.IsAdmin && existingProgram.OwnerId != currentUser.UserId)
                return (false, "You are not allowed to update this voting program.");

            existingProgram.ProgramName = model.ProgramName?.Trim() ?? string.Empty;
            existingProgram.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            existingProgram.StartTime   = model.StartTime;
            existingProgram.EndTime     = model.EndTime;
            existingProgram.IsPublished = model.IsPublished;
            existingProgram.Slug        = model.Slug;

            try
            {
                await context.SaveChangesAsync();
                logger.LogInformation("Voting program updated. ID: {ProgramId}", id);
                return (true, null);
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Database error while updating voting program {ProgramId}.", id);
                return (false, "The voting program could not be updated. Please try again.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while updating voting program {ProgramId}.", id);
                return (false, "An unexpected error occurred. Please try again.");
            }
        }

        // ── Voter Registration ────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<(bool success, string? errorMessage)> RegisterVoterByEmailAsync(
            int programId, string name, string email, string? memberId, string registrationSource)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
                return (false, "Name and Email are required.");

            email = email.Trim().ToLowerInvariant();

            if (!await ProgramExistsAsync(programId))
                return (false, "Voting program not found.");

            // FIX: The original code only checked for duplicate EMAIL.
            //      The DB unique constraint is on {ProgramId, UserId}.
            //      If the same Identity account is registered under two different
            //      emails, the DB would throw an unhandled exception.
            //      We now check BOTH email AND UserId.

            // Check 1: Duplicate email in this program
            bool emailTaken = await context.Voters
                .AnyAsync(v => v.ProgramId == programId &&
                               v.Email != null &&
                               v.Email.ToLower() == email);

            if (emailTaken)
                return (false, "This email is already registered for this program.");

            // Check 2: The Identity account linked to this email may already be
            //          registered (e.g. added earlier under a different email alias)
            var linkedUser = await userManager.FindByEmailAsync(email);
            if (linkedUser != null)
            {
                bool userIdTaken = await context.Voters
                    .AnyAsync(v => v.ProgramId == programId && v.UserId == linkedUser.Id);

                if (userIdTaken)
                    return (false, "This user account is already registered for this program.");
            }

            var voter = new Voter
            {
                Name               = name.Trim(),
                Email              = email,
                MemberId           = string.IsNullOrWhiteSpace(memberId) ? null : memberId.Trim(),
                ProgramId          = programId,
                UserId             = linkedUser?.Id,  // null if user hasn't registered yet
                RegisteredAt       = DateTime.UtcNow,
                RegistrationSource = registrationSource,
            };

            try
            {
                context.Voters.Add(voter);
                await context.SaveChangesAsync();
                logger.LogInformation(
                    "Voter '{Email}' registered for program {ProgramId} via '{Source}'.",
                    email, programId, registrationSource);
                return (true, null);
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Database error registering voter '{Email}' for program {ProgramId}.", email, programId);
                return (false, "Voter could not be registered. Please verify the details and try again.");
            }
        }

        /// <inheritdoc/>
        public async Task<(bool success, string? errorMessage)> RemoveVoterAsync(int voterId, int programId)
        {
            var voter = await context.Voters.FindAsync(voterId);
            if (voter == null || voter.ProgramId != programId)
                return (false, "Voter not found.");

            try
            {
                context.Voters.Remove(voter);
                await context.SaveChangesAsync();
                logger.LogInformation("Voter {VoterId} removed from program {ProgramId}.", voterId, programId);
                return (true, null);
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Database error removing voter {VoterId} from program {ProgramId}.", voterId, programId);
                return (false, "Voter could not be removed right now.");
            }
        }

        // ── Results ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<List<CandidateResultViewModel>> GetResultsAsync(int programId)
        {
            var program = await GetProgramWithCandidatesAsync(programId);
            if (program is null) return new List<CandidateResultViewModel>();

            var voteGroups = await context.Votes
                .Where(v => v.ProgramId == programId)
                .GroupBy(v => v.CandidateId)
                .Select(g => new { CandidateId = g.Key, VoteCount = g.Count() })
                .ToListAsync();

            return program.Candidates
                .Select(c => new CandidateResultViewModel
                {
                    Candidate = c,
                    VoteCount = voteGroups.FirstOrDefault(g => g.CandidateId == c.Id)?.VoteCount ?? 0
                })
                .OrderByDescending(r => r.VoteCount)
                .ToList();
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private async Task<(bool isValid, string? errorMessage, CurrentUserInfo? currentUser)> ValidateProgramAsync(
            VotingProgram model, int? existingProgramId = null)
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated != true)
                return (false, "You must be logged in to manage voting programs.", null);

            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return (false, "Unable to determine the current user. Please sign in again.", null);

            model.ProgramName = model.ProgramName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(model.ProgramName))
                return (false, "Program name is required.", null);

            if (model.StartTime == default || model.EndTime == default)
                return (false, "Please select valid start and end times.", null);

            var startUtc = NormalizeToUtc(model.StartTime);
            var endUtc   = NormalizeToUtc(model.EndTime);

            if (endUtc < startUtc.Add(MinimumProgramDuration))
                return (false, $"End time must be at least {MinimumProgramDuration.TotalMinutes} minutes after the start time.", null);

            model.StartTime = startUtc;
            model.EndTime   = endUtc;

            if (!TryNormalizeSlug(model.Slug, out var normalizedSlug))
                return (false, "Slug can only contain lowercase letters, numbers, hyphens, and underscores.", null);

            if (!string.IsNullOrWhiteSpace(normalizedSlug))
            {
                var slugInUse = await context.VotingPrograms.AnyAsync(p =>
                    p.Slug != null &&
                    p.Slug.ToLower() == normalizedSlug &&
                    (!existingProgramId.HasValue || p.Id != existingProgramId.Value));

                if (slugInUse)
                    return (false, "This custom URL slug is already taken. Please choose another.", null);
            }

            model.Slug = normalizedSlug;

            var displayName = httpContext.User.FindFirstValue(ClaimTypes.Name)
                           ?? httpContext.User.Identity?.Name
                           ?? "Unknown User";

            return (true, null, new CurrentUserInfo(userId, displayName, httpContext.User.IsInRole("Admin")));
        }

        private static DateTime NormalizeToUtc(DateTime value) =>
            value.Kind switch
            {
                DateTimeKind.Utc   => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _                  => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            };

        private static bool TryNormalizeSlug(string? rawSlug, out string? normalizedSlug)
        {
            if (string.IsNullOrWhiteSpace(rawSlug))
            {
                normalizedSlug = null;
                return true;
            }

            var cleaned = InvalidSlugCharacters.Replace(rawSlug.Trim().ToLowerInvariant(), "-");
            cleaned = cleaned.Trim('-', '_').Trim();
            normalizedSlug = string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
            return true;
        }

        private sealed record CurrentUserInfo(string UserId, string DisplayName, bool IsAdmin);
    }
}
