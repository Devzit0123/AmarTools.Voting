using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AmarTools.Voting.Models;   // For ApplicationUser

namespace AmarTools.Voting.Models
{
    /// <summary>
    /// Represents a person who is allowed to vote in a specific voting program.
    /// Only registered voters (by program owner) can cast a vote.
    /// </summary>
    public class Voter
    {
        [Key]
        public int Id { get; set; }

        // ── Identification ────────────────────────────────────────────────────
        [Required(ErrorMessage = "Name is required")]
        [StringLength(150, MinimumLength = 2,
            ErrorMessage = "Name must be between 2 and 150 characters")]
        public string Name { get; set; } = null!;

        /// <summary>
        /// Unique identifier within the program (student ID, employee number, etc.)
        /// </summary>
        [StringLength(100)]
        public string? MemberId { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email address")]
        [StringLength(200)]
        public string? Email { get; set; }

        [Phone(ErrorMessage = "Invalid phone number")]
        [StringLength(30)]
        public string? Phone { get; set; }

        // ── Important: Link to the actual logged-in user who will vote ───────
        public string? UserId { get; set; }

        public virtual ApplicationUser? User { get; set; }

        // ── Relationship with Program ─────────────────────────────────────────
        [Required]
        [ForeignKey(nameof(VotingProgram))]
        public int ProgramId { get; set; }

        public virtual VotingProgram VotingProgram { get; set; } = null!;

        // ── Audit ─────────────────────────────────────────────────────────────
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        public bool HasVoted { get; set; } = false;

        public DateTime? VotedAt { get; set; }

        [StringLength(50)]
        public string? RegistrationSource { get; set; }   // "owner" or "self"

        // ── Verification (optional future use) ────────────────────────────────
        public bool IsVerified { get; set; } = false;
        public DateTime? VerifiedAt { get; set; }
        [StringLength(50)]
        public string? VerificationMethod { get; set; }
    }
}
