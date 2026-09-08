using System.ComponentModel.DataAnnotations;
using AmarTools.Voting.Models;
using Xunit;

namespace AmarTools.Voting.UnitTests
{
    /// <summary>
    /// Boundary Value Analysis for [StringLength]/[Required] attributes declared
    /// directly on the domain models. Mirrors the BVA tab of the STQA workbook.
    /// </summary>
    public class ModelValidationTests
    {
        private static (bool isValid, List<ValidationResult> results) Validate(object model)
        {
            var results = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(
                model, new ValidationContext(model), results, validateAllProperties: true);
            return (isValid, results);
        }

        // ── M2: VotingProgram.ProgramName — [StringLength(200, MinimumLength = 3)] ──
        [Theory]
        [InlineData(2, false)]   // Min-1
        [InlineData(3, true)]    // Min
        [InlineData(4, true)]    // Min+1
        [InlineData(199, true)]  // Max-1
        [InlineData(200, true)]  // Max
        [InlineData(201, false)] // Max+1
        public void ProgramName_LengthBoundaries(int length, bool expectedValid)
        {
            var program = new VotingProgram
            {
                ProgramName = new string('P', length),
                StartTime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(2),
            };

            var (isValid, _) = Validate(program);

            Assert.Equal(expectedValid, isValid);
        }

        [Fact]
        public void ProgramName_Blank_IsRejected()
        {
            var program = new VotingProgram { ProgramName = "", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1) };
            var (isValid, results) = Validate(program);

            Assert.False(isValid);
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(VotingProgram.ProgramName)));
        }

        // ── M3: Candidate.Name — [StringLength(150, MinimumLength = 2)] ─────────────
        [Theory]
        [InlineData(1, false)]   // Min-1
        [InlineData(2, true)]    // Min
        [InlineData(3, true)]    // Min+1
        [InlineData(149, true)]  // Max-1
        [InlineData(150, true)]  // Max
        [InlineData(151, false)] // Max+1
        public void CandidateName_LengthBoundaries(int length, bool expectedValid)
        {
            var candidate = new Candidate
            {
                Name = new string('C', length),
                CandidateCode = "C1",
                ProgramId = 1,
            };

            var (isValid, _) = Validate(candidate);

            Assert.Equal(expectedValid, isValid);
        }

        // ── M3: Candidate.CandidateCode — [StringLength(50, MinimumLength = 1)] ─────
        [Theory]
        [InlineData(0, false)]  // Min-1 (empty)
        [InlineData(1, true)]   // Min
        [InlineData(50, true)]  // Max
        [InlineData(51, false)] // Max+1
        public void CandidateCode_LengthBoundaries(int length, bool expectedValid)
        {
            var candidate = new Candidate
            {
                Name = "Valid Name",
                CandidateCode = new string('X', length),
                ProgramId = 1,
            };

            var (isValid, _) = Validate(candidate);

            Assert.Equal(expectedValid, isValid);
        }

        // ── M4: Voter.Name — [StringLength(150, MinimumLength = 2)] ─────────────────
        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(150, true)]
        [InlineData(151, false)]
        public void VoterName_LengthBoundaries(int length, bool expectedValid)
        {
            var voter = new Voter { Name = new string('V', length), ProgramId = 1 };
            var (isValid, _) = Validate(voter);

            Assert.Equal(expectedValid, isValid);
        }

        [Theory]
        [InlineData("voter@example.com", true)]
        [InlineData("not-an-email", false)]
        [InlineData("", true)] // Email is optional on the model (nullable, no [Required])
        public void VoterEmail_FormatValidation_WhenModelBound(string email, bool expectedValid)
        {
            // NOTE: This attribute-level check only fires when the model is bound by
            // MVC / Validator. VotingService.RegisterVoterByEmailAsync takes raw strings
            // and never runs this validation — see BUG-004 in the Bug Report tab, and
            // the corresponding negative test in VotingServiceTests below.
            var voter = new Voter { Name = "Valid Name", Email = string.IsNullOrEmpty(email) ? null : email, ProgramId = 1 };
            var (isValid, _) = Validate(voter);

            Assert.Equal(expectedValid, isValid);
        }
    }
}
