using AmarTools.Voting.Models;
using Xunit;

namespace AmarTools.Voting.UnitTests
{
    /// <summary>
    /// Basis Path coverage for BlockchainVote.GenerateHash / IsValid (M4, method 5/5).
    /// See BasisPath tab rows tagged "BlockchainVote.GenerateHash/IsValid".
    /// </summary>
    public class BlockchainVoteTests
    {
        [Fact] // Path 1 — genesis block (no previous hash)
        public void GenerateHash_GenesisBlock_UsesZeroPlaceholder_ForPreviousHash()
        {
            var block = new BlockchainVote { VoteId = 1 };
            block.GenerateHash(previousHash: null!);

            Assert.NotNull(block.Hash);
            Assert.Equal(64, block.Hash.Length); // SHA-256 hex = 64 chars
            Assert.Null(block.PreviousHash);
        }

        [Fact] // Path 2 — chained block links correctly
        public void GenerateHash_ChainedBlock_LinksToPreviousHash_AndValidates()
        {
            var genesis = new BlockchainVote { VoteId = 1 };
            genesis.GenerateHash(null!);

            var next = new BlockchainVote { VoteId = 2 };
            next.GenerateHash(genesis.Hash);

            Assert.Equal(genesis.Hash, next.PreviousHash);
            Assert.True(next.IsValid(genesis.Hash));
        }

        [Fact] // Path 3 — tampering with VoteId after hashing invalidates the block
        public void IsValid_ReturnsFalse_WhenVoteIdTamperedAfterHashing()
        {
            var block = new BlockchainVote { VoteId = 1 };
            block.GenerateHash(previousHash: "0");

            block.VoteId = 999; // simulate tampering

            Assert.False(block.IsValid("0"));
        }

        [Fact] // Path 3 (variant) — tampering with the stored Hash directly
        public void IsValid_ReturnsFalse_WhenHashItselfIsTampered()
        {
            var block = new BlockchainVote { VoteId = 1 };
            block.GenerateHash(previousHash: "0");

            block.Hash = "0000000000000000000000000000000000000000000000000000000000000"; // corrupted

            Assert.False(block.IsValid("0"));
        }

        [Fact] // Path 4 — broken chain link: hash matches but previousHash argument doesn't
        public void IsValid_ReturnsFalse_WhenPreviousHashArgumentDoesNotMatchStoredValue()
        {
            var block = new BlockchainVote { VoteId = 1 };
            block.GenerateHash(previousHash: "abc123");

            // Hash itself is untouched and internally consistent, but caller passes the
            // wrong "previous block" hash — simulating a broken chain link / reordered chain.
            var isValid = block.IsValid("different-hash-than-was-used");

            Assert.False(isValid);
        }

        [Fact]
        public void ComputeHash_IsDeterministic_ForSameInput()
        {
            var input = "12345previous-hash2026-01-01T00:00:00.000Z";

            var hash1 = BlockchainVote.ComputeHash(input);
            var hash2 = BlockchainVote.ComputeHash(input);

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void ComputeHash_DifferentInputs_ProduceDifferentHashes()
        {
            var hashA = BlockchainVote.ComputeHash("input-a");
            var hashB = BlockchainVote.ComputeHash("input-b");

            Assert.NotEqual(hashA, hashB);
        }
    }
}
