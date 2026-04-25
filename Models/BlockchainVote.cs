using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace AmarTools.Voting.Models
{
    public class BlockchainVote
    {
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

        // FIX: Freeze timestamp at object creation so it matches what GenerateHash
        // will use. Previously the default was DateTime.UtcNow at property
        // declaration time, but GenerateHash was called later – if any delay
        // existed (however small) the recomputed hash in BlockchainService would
        // not match the stored hash because the timestamp used to compute it was
        // the same frozen value. This is safe because GenerateHash is always
        // called immediately after construction before SaveChanges.
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // ── Hashing ───────────────────────────────────────────────────────────

        public static string ComputeHash(string input)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant(); // .NET 5+ one-liner
        }

        /// <summary>
        /// Computes and stores the hash for this block.
        /// Call this immediately after setting VoteId, before SaveChanges.
        /// </summary>
        public void GenerateHash(string previousHash)
        {
            PreviousHash = previousHash;
            // FIX: Timestamp is already set at construction. We use the same
            // format string here and in BlockchainService so hashes match.
            string data = $"{VoteId}{PreviousHash ?? "0"}{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}";
            Hash = ComputeHash(data);
        }

        public bool IsValid(string previousHash)
        {
            string data = $"{VoteId}{previousHash ?? "0"}{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}";
            return Hash == ComputeHash(data) && PreviousHash == previousHash;
        }
    }
}
