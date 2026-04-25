using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AmarTools.Voting.Data
{
    public class VotingDbContext(DbContextOptions<VotingDbContext> options)
        : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<VotingProgram> VotingPrograms { get; set; } = null!;
        public DbSet<Candidate> Candidates { get; set; } = null!;
        public DbSet<Voter> Voters { get; set; } = null!;
        public DbSet<Vote> Votes { get; set; } = null!;
        public DbSet<BlockchainVote> BlockchainVotes { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── VotingProgram Relationships ─────────────────────────────────────
            modelBuilder.Entity<VotingProgram>()
                .HasOne(p => p.Owner)
                .WithMany()                    // No navigation back to programs on User
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);   // Owner deleted → programs deleted

            // ── Voter → User Relationship ───────────────────────────────────────
            modelBuilder.Entity<Voter>()
                .HasOne(v => v.User)
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.NoAction);  // Prevent cascade conflicts

            // ── Critical Unique Constraints ─────────────────────────────────────

            // Prevent double voting (most important!)
            modelBuilder.Entity<Vote>()
                .HasIndex(v => new { v.VoterId, v.ProgramId })
                .IsUnique();

            // Candidate code must be unique per program
            modelBuilder.Entity<Candidate>()
                .HasIndex(c => new { c.ProgramId, c.CandidateCode })
                .IsUnique();

            // One user can only be registered once per program
            modelBuilder.Entity<Voter>()
                .HasIndex(v => new { v.ProgramId, v.UserId })
                .IsUnique();

            // ── Performance Indexes ─────────────────────────────────────────────
            modelBuilder.Entity<Vote>().HasIndex(v => new { v.ProgramId, v.CandidateId });
            modelBuilder.Entity<Vote>().HasIndex(v => v.ProgramId);
            modelBuilder.Entity<Vote>().HasIndex(v => v.VotedAt);
            modelBuilder.Entity<Candidate>().HasIndex(c => c.ProgramId);
            modelBuilder.Entity<Voter>().HasIndex(v => v.ProgramId);
            modelBuilder.Entity<BlockchainVote>().HasIndex(b => new { b.VoteId, b.Timestamp });
            modelBuilder.Entity<BlockchainVote>().HasIndex(b => b.VoteId);

            // ── Column Configurations ───────────────────────────────────────────
            modelBuilder.Entity<VotingProgram>()
                .Property(p => p.ProgramName)
                .HasMaxLength(200)
                .IsRequired();

            modelBuilder.Entity<VotingProgram>()
                .Property(p => p.Description)
                .HasMaxLength(2000);

            modelBuilder.Entity<VotingProgram>()
                .Property(p => p.Slug)
                .HasMaxLength(150);

            modelBuilder.Entity<Candidate>()
                .Property(c => c.Name)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<Candidate>()
                .Property(c => c.CandidateCode)
                .HasMaxLength(50)
                .IsRequired();

            modelBuilder.Entity<Candidate>()
                .Property(c => c.ImageUrl)
                .HasMaxLength(500);

            modelBuilder.Entity<Voter>()
                .Property(v => v.Name)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<Voter>()
                .Property(v => v.Email)
                .HasMaxLength(200);

            modelBuilder.Entity<Voter>()
                .Property(v => v.MemberId)
                .HasMaxLength(100);

            modelBuilder.Entity<Vote>()
                .Property(v => v.IpAddress)
                .HasMaxLength(45);

            modelBuilder.Entity<Vote>()
                .Property(v => v.UserAgent)
                .HasMaxLength(512);

            modelBuilder.Entity<BlockchainVote>()
                .Property(b => b.Hash)
                .HasMaxLength(64)
                .IsRequired();

            modelBuilder.Entity<BlockchainVote>()
                .Property(b => b.PreviousHash)
                .HasMaxLength(64);
        }
    }
}