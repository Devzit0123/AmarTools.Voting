using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AmarTools.Voting.Models
{
    public class Candidate
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Candidate name is required")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 150 characters")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(500)]
        public string? ImageUrl { get; set; }

        [Required(ErrorMessage = "Candidate code is required")]
        [StringLength(50, MinimumLength = 1)]
        public string CandidateCode { get; set; } = string.Empty;

        [Required]
        public int ProgramId { get; set; }

        [ForeignKey(nameof(ProgramId))]
        public virtual VotingProgram VotingProgram { get; set; } = null!;

        public virtual ICollection<Vote> Votes { get; set; } = new List<Vote>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public int VoteCount { get; set; }
    }
}