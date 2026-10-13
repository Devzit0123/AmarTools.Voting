using AmarTools.Voting.Controllers;
using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  Shared helpers (self-contained so this file does not depend on private
//  helpers inside ProgramOwnerControllerTests.cs)
// ─────────────────────────────────────────────────────────────────────────────
internal static class OwnerTestKit
{
    public static VotingDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<VotingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new VotingDbContext(builder.Options);
    }

    public static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var m = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        m.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        m.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Owner", UserName = "owner" });
        return m;
    }

    public static ProgramOwnerController CreateController(
        VotingDbContext context,
        IVotingService? votingService = null,
        IBlockchainService? blockchainService = null,
        string? userId = "owner-1",
        bool admin = false)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new ProgramOwnerController(
            context,
            votingService ?? Mock.Of<IVotingService>(),
            blockchainService ?? Mock.Of<IBlockchainService>(),
            MockUserManager().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
        SetUser(controller, userId, admin);
        return controller;
    }

    public static void SetUser(Controller controller, string? userId, bool admin = false)
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    public static VotingService CreateService(VotingDbContext context, string userId = "owner-1", bool admin = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "Owner")
        };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        var accessor = new HttpContextAccessor { HttpContext = http };

        return new VotingService(
            context, accessor, MockUserManager().Object, Mock.Of<ILogger<VotingService>>());
    }

    // Program states
    public static VotingProgram Upcoming(int id, string ownerId) => new()
    {
        Id = id,
        ProgramName = $"Program {id}",
        OwnerId = ownerId,
        StartTime = DateTime.UtcNow.AddHours(1),
        EndTime = DateTime.UtcNow.AddHours(2),
        IsPublished = false
    };

    public static VotingProgram Active(int id, string ownerId) => new()
    {
        Id = id,
        ProgramName = $"Program {id}",
        OwnerId = ownerId,
        StartTime = DateTime.UtcNow.AddHours(-1),
        EndTime = DateTime.UtcNow.AddHours(1),
        IsPublished = true
    };

    public static VotingProgram Ended(int id, string ownerId) => new()
    {
        Id = id,
        ProgramName = $"Program {id}",
        OwnerId = ownerId,
        StartTime = DateTime.UtcNow.AddHours(-2),
        EndTime = DateTime.UtcNow.AddHours(-1),
        IsPublished = true
    };

    public static Candidate Candidate(int id, int programId, string code = "C1") => new()
    {
        Id = id,
        ProgramId = programId,
        Name = $"Candidate {id}",
        CandidateCode = code
    };

    public static async Task SeedAsync(VotingDbContext context, params object[] entities)
    {
        context.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public static bool HasValidationError(object instance, string member)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results.Any(r => r.MemberNames.Contains(member));
    }

    public static bool Errored(ProgramOwnerController c) => c.TempData.ContainsKey("Error");
}

/// <summary>
/// Fault injection for the catch (DbUpdateException) paths: seed normally, then set
/// <see cref="Fail"/> so the next SaveChanges throws as a database constraint failure would.
/// </summary>
internal sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public bool Fail { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Fail) throw new DbUpdateException("Simulated database failure.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
