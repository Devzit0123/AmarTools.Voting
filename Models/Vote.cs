using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AmarTools.Voting.Models
{
    /// <summary>
    /// Represents a single vote cast in a voting program.
    /// </summary>
    public class Vote
    {
        private DateTime? verifiedAt;

        [Key]
        public int Id { get; set; }

        // ── Foreign Keys ──────────────────────────────────────────────────────

        [Required]
        public int ProgramId { get; set; }

        [ForeignKey(nameof(ProgramId))]
        public virtual VotingProgram VotingProgram { get; set; } = null!;

        [Required]
        public int CandidateId { get; set; }

        [ForeignKey(nameof(CandidateId))]
        public virtual Candidate Candidate { get; set; } = null!;

        [Required]
        public int VoterId { get; set; }                    // Links to Voter table (not directly to User)

        [ForeignKey(nameof(VoterId))]
        public virtual Voter Voter { get; set; } = null!;

        // ── Core Fields ───────────────────────────────────────────────────────

        public DateTime VotedAt { get; set; } = DateTime.UtcNow;

        [StringLength(45)]
        public string? IpAddress { get; set; }

        [StringLength(512)]
        public string? UserAgent { get; set; }

        [StringLength(50)]
        public string? VoteSource { get; set; } = "web";

        public bool IsVerified { get; set; } = false;

        public DateTime? VerifiedAt { get => verifiedAt; set => verifiedAt = value; }
        // Optional: For future blockchain reference
        [StringLength(100)]
        public string? BlockchainReference { get; set; }
    }
}