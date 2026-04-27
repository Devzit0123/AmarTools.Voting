using AmarTools.Voting.Models;

namespace AmarTools.Voting.Services
{
    public interface IVotingService
    {
        // ── Programs ──────────────────────────────────────────────────────────
        Task<List<VotingProgram>> GetAllProgramsForDashboardAsync();
        Task<VotingProgram?> GetProgramWithCandidatesAsync(int programId);
        Task<bool> ProgramExistsAsync(int id);
        Task<(bool success, string? errorMessage)> ValidateAndCreateProgramAsync(VotingProgram model);
        Task<(bool success, string? errorMessage)> ValidateAndUpdateProgramAsync(int id, VotingProgram model);

       
        Task<(bool success, string? errorMessage)> RegisterVoterByEmailAsync(
            int programId, string name, string email, string? memberId, string registrationSource);

        
        Task<(bool success, string? errorMessage)> RemoveVoterAsync(int voterId, int programId);

       
        Task<List<CandidateResultViewModel>> GetResultsAsync(int programId);
    }
}
