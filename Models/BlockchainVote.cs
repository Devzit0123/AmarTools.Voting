using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace AmarTools.Voting.Models
{
    public class BlockchainVote
    {
        /// <summary>
        /// Version 1 (legacy) hashed only VoteId + previous hash + timestamp, so editing a
        /// vote's candidate went unnoticed (D18). Version 2 also hashes the vote's content.
        /// Existing v1 blocks keep validating with the v1 formula.
        /// </summary>
        public const int CurrentHashVersion = 2;

        private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Vote))]
        public int VoteId { get; set; }

        public virtual Vote Vote { get; set; } = null!;

        [Required]
        [StringLength(64)]
        public string Hash { get; set; } = null!;

        [StringLength(64)]
        public string? PreviousHash { get; set; }

        // Frozen at construction so GenerateHash and later validation use the same value.
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>Hash formula used for this block (1 = legacy, 2 = includes vote content).</summary>
        public int HashVersion { get; set; } = CurrentHashVersion;

        // ── Hashing ───────────────────────────────────────────────────────────

        public static string ComputeHash(string input)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>The exact text that is hashed for this block, given its vote.</summary>
        public string BuildPayload(Vote vote, string previousHash) => HashVersion switch
        {
            1 => $"{VoteId}{previousHash}{Timestamp.ToString(TimeFormat)}",
            _ => string.Join('|',
                    "v2",
                    VoteId,
                    vote.ProgramId,
                    vote.CandidateId,
                    vote.VoterId,
                    vote.VotedAt.ToString(TimeFormat),
                    previousHash,
                    Timestamp.ToString(TimeFormat))
        };

        /// <summary>
        /// Computes and stores the hash for this block. Call it after setting VoteId,
        /// before SaveChanges. <paramref name="vote"/> must be the saved vote row.
        /// </summary>
        public void GenerateHash(Vote vote, string previousHash)
        {
            if (vote.Id != VoteId)
                throw new ArgumentException("The vote does not belong to this block.", nameof(vote));

            HashVersion = CurrentHashVersion;
            PreviousHash = previousHash;
            Hash = ComputeHash(BuildPayload(vote, previousHash));
        }

        /// <summary>True when the stored hash matches the vote's current content and the expected previous hash.</summary>
        public bool IsValid(Vote vote, string previousHash) =>
            vote.Id == VoteId &&
            PreviousHash == previousHash &&
            Hash == ComputeHash(BuildPayload(vote, previousHash));
    }
}
