using System.ComponentModel.DataAnnotations;
using AmarTools.Voting.Models;

namespace AmarTools.Voting.Tests;

public class VotingProgramBoundaryTests
{
    private static bool IsValid(VotingProgram model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();

        return Validator.TryValidateObject(
            model,
            context,
            results,
            validateAllProperties: true);
    }

    private static VotingProgram CreateValidProgram()
    {
        return new VotingProgram
        {
            ProgramName = "Test Program",
            Description = "Test description",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1)
        };
    }


    

    [Theory]
    [InlineData(2, false)]     
    [InlineData(3, true)]      
    [InlineData(4, true)]      
    [InlineData(199, true)]    
    [InlineData(200, true)]    
    [InlineData(201, false)]   
    public void ProgramName_BoundaryValues(
        int length,
        bool expectedValid)
    {
        var model = CreateValidProgram();

        model.ProgramName = new string('A', length);

        
        bool actualValid = IsValid(model);

        Assert.Equal(expectedValid, actualValid);
    }



    [Theory]
    [InlineData(1999, true)]   
    [InlineData(2000, true)]   
    [InlineData(2001, false)]  
    public void Description_BoundaryValues(
        int length,
        bool expectedValid)
    {
        
        var model = CreateValidProgram();

        model.Description = new string('A', length);

        
        bool actualValid = IsValid(model);

        
        Assert.Equal(expectedValid, actualValid);
    }
}
