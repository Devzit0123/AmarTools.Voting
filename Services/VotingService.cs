using System.Security.Claims;
using System.Text.RegularExpressions;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AmarTools.Voting.Services
{
    public class VotingService(
        VotingDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<VotingService> logger) : IVotingService
    {
        private static readonly TimeSpan MinimumProgramDuration = TimeSpan.FromMinutes(5);
        private static readonly Regex InvalidSlugCharacters = new("[^a-z0-9_-]+", RegexOptions.Compiled);

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
            {
                return (false, errorMessage);
            }

            model.OwnerId = currentUser.UserId;
            model.CreatedBy = currentUser.DisplayName;
            model.CreatedAt = DateTime.UtcNow;

            try
            {
                context.VotingPrograms.Add(model);
                await context.SaveChangesAsync();
                logger.LogInformation("Voting program created successfully. ID: {ProgramId}, Owner: {UserId}",
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
            {
                return (false, "Voting program not found.");
            }

            var (isValid, errorMessage, currentUser) = await ValidateProgramAsync(model, existingProgram.Id);
            if (!isValid || currentUser is null)
            {
                return (false, errorMessage);
            }

            if (!currentUser.IsAdmin && existingProgram.OwnerId != currentUser.UserId)
            {
                return (false, "You are not allowed to update this voting program.");
            }

            existingProgram.ProgramName = model.ProgramName?.Trim() ?? string.Empty;
            existingProgram.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            existingProgram.StartTime = model.StartTime;
            existingProgram.EndTime = model.EndTime;
            existingProgram.IsPublished = model.IsPublished;
            existingProgram.Slug = model.Slug;

            try
            {
                await context.SaveChangesAsync();
                logger.LogInformation("Voting program updated successfully. ID: {ProgramId}", id);
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

        private async Task<(bool isValid, string? errorMessage, CurrentUserInfo? currentUser)> ValidateProgramAsync(
            VotingProgram model, int? existingProgramId = null)
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated != true)
            {
                return (false, "You must be logged in to manage voting programs.", null);
            }

            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return (false, "Unable to determine the current user. Please sign in again.", null);
            }

            // Basic validation
            model.ProgramName = model.ProgramName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(model.ProgramName))
            {
                return (false, "Program name is required.", null);
            }

            if (model.StartTime == default || model.EndTime == default)
            {
                return (false, "Please select valid start and end times.", null);
            }

            var startUtc = NormalizeToUtc(model.StartTime);
            var endUtc = NormalizeToUtc(model.EndTime);

            if (endUtc < startUtc.Add(MinimumProgramDuration))
            {
                return (false, $"End time must be at least {MinimumProgramDuration.TotalMinutes} minutes after the start time.", null);
            }

            model.StartTime = startUtc;
            model.EndTime = endUtc;

            // Slug validation
            if (!TryNormalizeSlug(model.Slug, out var normalizedSlug))
            {
                return (false, "Slug can only contain lowercase letters, numbers, hyphens, and underscores.", null);
            }

            if (!string.IsNullOrWhiteSpace(normalizedSlug))
            {
                var slugInUse = await context.VotingPrograms.AnyAsync(p =>
                    p.Slug != null &&
                    p.Slug.ToLower() == normalizedSlug &&
                    (!existingProgramId.HasValue || p.Id != existingProgramId.Value));

                if (slugInUse)
                {
                    return (false, "This custom URL slug is already taken. Please choose another.", null);
                }
            }

            model.Slug = normalizedSlug;

            var displayName = httpContext.User.FindFirstValue(ClaimTypes.Name)
                           ?? httpContext.User.Identity?.Name
                           ?? "Unknown User";

            return (true, null, new CurrentUserInfo(userId, displayName, httpContext.User.IsInRole("Admin")));
        }

        private static DateTime NormalizeToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            };
        }

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