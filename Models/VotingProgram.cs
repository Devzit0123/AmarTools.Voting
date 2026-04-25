using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AmarTools.Voting.Models
{
    /// <summary>
    /// Represents a single voting program (election, poll, survey, etc.).
    /// Each program has an Owner (the user who created it).
    /// </summary>
    public class VotingProgram
    {
        [Key]
        public int Id { get; set; }

        // ── Identity & Description ────────────────────────────────────────────
        [Required(ErrorMessage = "Program name is required")]
        [StringLength(200, MinimumLength = 3,
            ErrorMessage = "Name must be between 3 and 200 characters")]
        [Display(Name = "Program Name")]
        public string ProgramName { get; set; } = string.Empty;

        [StringLength(2000, ErrorMessage = "Description cannot exceed 2000 characters")]
        [DataType(DataType.MultilineText)]
        public string? Description { get; set; }

        // ── Time Window (Always stored as UTC) ────────────────────────────────
        [Required]
        [DataType(DataType.DateTime)]
        [Display(Name = "Start Time")]
        public DateTime StartTime { get; set; }

        [Required]
        [DataType(DataType.DateTime)]
        [Display(Name = "End Time")]
        public DateTime EndTime { get; set; }

        [Display(Name = "Published / Active")]
        public bool IsPublished { get; set; } = false;

        // ── Owner ─────────────────────────────────────────────────────────────
        [ValidateNever]
        public string OwnerId { get; set; } = string.Empty;

        [ValidateNever]
        public virtual ApplicationUser Owner { get; set; } = null!;

        // ── Navigation Properties ─────────────────────────────────────────────
        public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
        public virtual ICollection<Voter> Voters { get; set; } = new List<Voter>();
        public virtual ICollection<Vote> Votes { get; set; } = new List<Vote>();

        // ── Audit Fields ──────────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(150)]
        [RegularExpression(
            @"^[a-zA-Z0-9]+(?:[-_][a-zA-Z0-9]+)*$",
            ErrorMessage = "Slug can contain only letters, numbers, hyphens, and underscores.")]
        public string? Slug { get; set; }

        // ── Computed / Business Properties (Not Mapped) ───────────────────────
        [NotMapped]
        public bool IsActive => IsPublished && DateTime.UtcNow >= StartTime && DateTime.UtcNow <= EndTime;

        [NotMapped]
        public bool HasStarted => DateTime.UtcNow >= StartTime;

        [NotMapped]
        public bool HasEnded => DateTime.UtcNow > EndTime;

        [NotMapped]
        public TimeSpan TimeRemaining => EndTime > DateTime.UtcNow
            ? EndTime - DateTime.UtcNow
            : TimeSpan.Zero;

        [NotMapped]
        public int CandidateCount => Candidates?.Count ?? 0;

        [NotMapped]
        public int TotalVotes => Votes?.Count ?? 0;

        // ── Helper Methods ────────────────────────────────────────────────────
        /// <summary>
        /// Validates that the end time is after the start time.
        /// </summary>
        public bool IsValidTimeRange()
        {
            return EndTime > StartTime;
        }

        public bool HasMinimumDuration(TimeSpan minimumDuration)
        {
            return EndTime >= StartTime.Add(minimumDuration);
        }

        /// <summary>
        /// Returns a user-friendly status string.
        /// </summary>
        [NotMapped]
        public string StatusDisplay
        {
            get
            {
                if (!IsPublished) return "Draft";
                if (IsActive) return "Active";
                if (HasEnded) return "Ended";
                return "Upcoming";
            }
        }
    }
}
