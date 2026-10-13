using System.ComponentModel.DataAnnotations;
using AmarTools.Voting.Models;
namespace AmarTools.Voting.Tests;
internal static class EpValidation
{
    public static bool IsValid(object model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(
            model,
            context,
            results,
            validateAllProperties: true);
    }
}
public class VotingProgramEquivalenceTests
{
    private static VotingProgram ValidProgram() => new()
    {
        ProgramName = "Campus Election 2026",
        Description = "Annual student council election",
        StartTime = DateTime.UtcNow,
        EndTime = DateTime.UtcNow.AddHours(1)
    };
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("A", false)]
    [InlineData("Campus Election 2026", true)]
    public void ProgramName_Partitions(string? name, bool expectedValid)
    {
        var model = ValidProgram();
        model.ProgramName = name!;
        Assert.Equal(expectedValid, EpValidation.IsValid(model));
    }
    [Fact]
    public void ProgramName_TooLong_IsInvalid()
    {
        var model = ValidProgram();
        model.ProgramName = new string('A', 500);
        Assert.False(EpValidation.IsValid(model));
    }
    [Theory]
    [InlineData(null, true)]
    [InlineData("A short description", true)]
    public void Description_Partitions(string? description, bool expectedValid)
    {
        var model = ValidProgram();
        model.Description = description;
        Assert.Equal(expectedValid, EpValidation.IsValid(model));
    }
    [Fact]
    public void Description_TooLong_IsInvalid()
    {
        var model = ValidProgram();
        model.Description = new string('D', 3000);
        Assert.False(EpValidation.IsValid(model));
    }
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("election", true)]
    [InlineData("2026", true)]
    [InlineData("campus-election-2026", true)]
    [InlineData("campus_election", true)]
    [InlineData("Campus", true)]
    [InlineData("campus election", false)]
    [InlineData("campus!", false)]
    [InlineData("-campus", false)]
    [InlineData("campus-", false)]
    [InlineData("campus--election", false)]
    public void Slug_Partitions(string? slug, bool expectedValid)
    {
        var model = ValidProgram();
        model.Slug = slug;
        Assert.Equal(expectedValid, EpValidation.IsValid(model));
    }
    [Fact]
    public void Slug_TooLong_IsInvalid()
    {
        var model = ValidProgram();
        model.Slug = new string('a', 200);
        Assert.False(EpValidation.IsValid(model));
    }
    [Theory]
    [InlineData(true, -30, false)]
    [InlineData(true, 30, true)]
    [InlineData(true, 120, false)]
    [InlineData(false, 30, false)]
    public void IsOpenAt_Partitions(
        bool isPublished,
        int minutesFromStart,
        bool expectedOpen)
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var program = new VotingProgram
        {
            ProgramName = "EP Program",
            StartTime = start,
            EndTime = start.AddHours(1),
            IsPublished = isPublished
        };
        Assert.Equal(expectedOpen, program.IsOpenAt(start.AddMinutes(minutesFromStart)));
    }
    [Theory]
    [InlineData(false, -1, 1, "Draft")]
    [InlineData(false, -3, -2, "Draft")]
    [InlineData(true, 1, 2, "Upcoming")]
    [InlineData(true, -1, 1, "Active")]
    [InlineData(true, -3, -2, "Ended")]
    public void StatusDisplay_Partitions(
        bool isPublished,
        int startHoursFromNow,
        int endHoursFromNow,
        string expectedStatus)
    {
        var program = new VotingProgram
        {
            ProgramName = "EP Program",
            IsPublished = isPublished,
            StartTime = DateTime.UtcNow.AddHours(startHoursFromNow),
            EndTime = DateTime.UtcNow.AddHours(endHoursFromNow)
        };
        Assert.Equal(expectedStatus, program.StatusDisplay);
    }
    [Theory]
    [InlineData(60, true)]
    [InlineData(-60, false)]
    public void IsValidTimeRange_Partitions(int endMinutesAfterStart, bool expected)
    {
        var start = DateTime.UtcNow;
        var program = new VotingProgram
        {
            ProgramName = "EP Program",
            StartTime = start,
            EndTime = start.AddMinutes(endMinutesAfterStart)
        };
        Assert.Equal(expected, program.IsValidTimeRange());
    }
}
public class CandidateEquivalenceTests
{
    private static Candidate ValidCandidate() => new()
    {
        Name = "Rahim Uddin",
        CandidateCode = "C01",
        ProgramId = 1
    };
    [Theory]
    [InlineData("", false)]
    [InlineData("A", false)]
    [InlineData("Rahim Uddin", true)]
    public void Name_Partitions(string name, bool expectedValid)
    {
        var model = ValidCandidate();
        model.Name = name;
        Assert.Equal(expectedValid, EpValidation.IsValid(model));
    }
    [Fact]
    public void Name_TooLong_IsInvalid()
    {
        var model = ValidCandidate();
        model.Name = new string('N', 400);
        Assert.False(EpValidation.IsValid(model));
    }
    [Theory]
    [InlineData("", false)]
    [InlineData("C01", true)]
    public void CandidateCode_Partitions(string code, bool expectedValid)
    {
        var model = ValidCandidate();
        model.CandidateCode = code;
        Assert.Equal(expectedValid, EpValidation.IsValid(model));
    }
    [Fact]
    public void CandidateCode_TooLong_IsInvalid()
    {
        var model = ValidCandidate();
        model.CandidateCode = new string('C', 100);
        Assert.False(EpValidation.IsValid(model));
    }
}
public class VoterEquivalenceTests
{
    private static Voter ValidVoter() => new()
    {
        Name = "Karim Hossain",
        ProgramId = 1
    };
    [Theory]
    [InlineData("", false)]
    [InlineData("A", false)]
    [InlineData("Karim Hossain", true)]
    public void Name_Partitions(string name, bool expectedValid)
    {
        var model = ValidVoter();
        model.Name = name;
        Assert.Equal(expectedValid, EpValidation.IsValid(model));
    }
    [Fact]
    public void Name_TooLong_IsInvalid()
    {
        var model = ValidVoter();
        model.Name = new string('V', 400);
        Assert.False(EpValidation.IsValid(model));
    }
    [Theory]
    [InlineData(null, true)]
    [InlineData("student@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("student@", false)]
    public void Email_Partitions(string? email, bool expectedValid)
    {
        var model = ValidVoter();
        model.Email = email;
        Assert.Equal(expectedValid, EpValidation.IsValid(model));
    }
    [Theory]
    [InlineData(null, true)]
    [InlineData("01712345678", true)]
    [InlineData("abc", false)]
    public void Phone_Partitions(string? phone, bool expectedValid)
    {
        var model = ValidVoter();
        model.Phone = phone;
        Assert.Equal(expectedValid, EpValidation.IsValid(model));
    }
}