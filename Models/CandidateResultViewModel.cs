namespace AmarTools.Voting.Models
{
    public class CandidateResultViewModel
    {
        public Candidate Candidate { get; set; } = null!;
        public int VoteCount { get; set; }
    }
}