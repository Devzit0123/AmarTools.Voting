using AmarTools.Voting.Models;

namespace AmarTools.Voting.Services
{
    public interface IVotingService
    {
        Task<List<VotingProgram>> GetAllProgramsForDashboardAsync();
        Task<VotingProgram?> GetProgramWithCandidatesAsync(int programId);
        Task<bool> ProgramExistsAsync(int id);
        Task<(bool success, string? errorMessage)> ValidateAndCreateProgramAsync(VotingProgram model);
        Task<(bool success, string? errorMessage)> ValidateAndUpdateProgramAsync(int id, VotingProgram model);
    }
}