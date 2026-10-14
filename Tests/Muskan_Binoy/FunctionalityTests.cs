using AmarTools.Voting.Controllers;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using AmarTools.Voting.Services.Background;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using System.Text;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  Functionality added after the codebase assessment:
//    F01  /v/{slug} opens a program through its custom slug (was D31: slug unused)
//    F02  Results can be exported as CSV by the owner and the admin
//    F03  Admin Delete refuses programs with recorded votes (D05 fix was owner-only)
//    F04  The published link uses the slug when one is set
//
//    dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "FullyQualifiedName~FunctionalityTests"
// ─────────────────────────────────────────────────────────────────────────────
[Trait("Category", "Functionality")]
public sealed class FunctionalityTests
{
    private static VotingController CreateVotingController(Data.VotingDbContext context) =>
        new(context, Mock.Of<IVotingService>(), Mock.Of<IBlockchainService>(), Mock.Of<IVoteBlockQueue>());

    private static VotingAdminController CreateAdminController(Data.VotingDbContext context, IVotingService? service = null)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new VotingAdminController(
            context,
            Mock.Of<IBlockchainService>(),
            service ?? Mock.Of<IVotingService>(),
            OwnerTestKit.MockUserManager().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
        OwnerTestKit.SetUser(controller, "admin-1", admin: true);
        return controller;
    }

    private static VotingProgram WithSlug(VotingProgram program, string slug)
    {
        program.Slug = slug;
        return program;
    }

    // ── F01: slug routing ───────────────────────────────────────────────────

    [Fact]
    public async Task F01_BySlug_PublishedProgram_RedirectsToItsVotePage()
    {
        using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, WithSlug(OwnerTestKit.Active(7, "owner-1"), "campus-vote"));

        var result = await CreateVotingController(context).BySlug("campus-vote");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(VotingController.Vote), redirect.ActionName);
        Assert.Equal(7, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task F01_BySlug_IsCaseInsensitiveAndTrimmed()
    {
        using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, WithSlug(OwnerTestKit.Active(7, "owner-1"), "campus-vote"));

        var result = await CreateVotingController(context).BySlug("  Campus-Vote ");

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task F01_BySlug_UnpublishedProgram_ReturnsNotFound()
    {
        using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, WithSlug(OwnerTestKit.Upcoming(7, "owner-1"), "draft-vote"));

        var result = await CreateVotingController(context).BySlug("draft-vote");

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("no-such-slug")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task F01_BySlug_UnknownOrEmptySlug_ReturnsNotFound(string slug)
    {
        using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, WithSlug(OwnerTestKit.Active(7, "owner-1"), "campus-vote"));

        var result = await CreateVotingController(context).BySlug(slug);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── F02: CSV export ─────────────────────────────────────────────────────

    private static async Task<Data.VotingDbContext> SeedProgramWithVotesAsync()
    {
        var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context,
            OwnerTestKit.Ended(1, "owner-1"),
            OwnerTestKit.Candidate(1, 1, "A"),
            OwnerTestKit.Candidate(2, 1, "B"),
            new Vote { Id = 1, ProgramId = 1, CandidateId = 2, VoterId = 1 },
            new Vote { Id = 2, ProgramId = 1, CandidateId = 2, VoterId = 2 },
            new Vote { Id = 3, ProgramId = 1, CandidateId = 1, VoterId = 3 });
        return context;
    }

    private static string CsvText(IActionResult result)
    {
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.EndsWith(".csv", file.FileDownloadName);
        return Encoding.UTF8.GetString(file.FileContents).TrimStart('﻿');
    }

    [Fact]
    public async Task F02_OwnerExport_ListsCandidatesByVotesWithPercentages()
    {
        using var context = await SeedProgramWithVotesAsync();
        var controller = OwnerTestKit.CreateController(context, OwnerTestKit.CreateService(context));

        var lines = CsvText(await controller.ExportResults(1))
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Rank,Candidate Code,Candidate Name,Votes,Percentage", lines[0]);
        Assert.Equal("1,\"B\",\"Candidate 2\",2,66.67", lines[1]);
        Assert.Equal("2,\"A\",\"Candidate 1\",1,33.33", lines[2]);
        Assert.Equal(",,Total,3,100.00", lines[3]);
    }

    [Fact]
    public async Task F02_OwnerExport_OtherOwnersProgram_IsForbidden()
    {
        using var context = await SeedProgramWithVotesAsync();
        var controller = OwnerTestKit.CreateController(context, OwnerTestKit.CreateService(context), userId: "owner-2");

        Assert.IsType<ForbidResult>(await controller.ExportResults(1));
    }

    [Fact]
    public async Task F02_OwnerExport_MissingProgram_ReturnsNotFound()
    {
        using var context = OwnerTestKit.CreateContext();
        var controller = OwnerTestKit.CreateController(context, OwnerTestKit.CreateService(context));

        Assert.IsType<NotFoundResult>(await controller.ExportResults(99));
    }

    [Fact]
    public async Task F02_AdminExport_AnyProgram_ReturnsCsv()
    {
        using var context = await SeedProgramWithVotesAsync();
        var controller = CreateAdminController(context, OwnerTestKit.CreateService(context, "admin-1", admin: true));

        Assert.Contains(",,Total,3,100.00", CsvText(await controller.ExportResults(1)));
    }

    [Fact]
    public async Task F02_Export_ProgramWithoutVotes_ShowsZeroPercent()
    {
        using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Ended(1, "owner-1"), OwnerTestKit.Candidate(1, 1, "A"));
        var controller = OwnerTestKit.CreateController(context, OwnerTestKit.CreateService(context));

        Assert.Contains("1,\"A\",\"Candidate 1\",0,0.00", CsvText(await controller.ExportResults(1)));
    }

    [Theory]
    [InlineData("Alice", "\"Alice\"")]
    [InlineData("O\"Neil", "\"O\"\"Neil\"")]                     // quotes are doubled
    [InlineData("Smith, John", "\"Smith, John\"")]              // commas stay inside the field
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")] // formula neutralised
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData(null, "\"\"")]
    public void F02_CsvField_QuotesAndBlocksSpreadsheetFormulas(string? value, string expected)
    {
        Assert.Equal(expected, ResultsCsv.Field(value));
    }

    // ── F03: admin delete with votes ────────────────────────────────────────

    [Fact]
    public async Task F03_AdminDelete_EndedProgramWithVotes_IsRefused()
    {
        using var context = await SeedProgramWithVotesAsync();
        var controller = CreateAdminController(context);

        await controller.Delete(1);

        Assert.Single(context.VotingPrograms);
        Assert.Equal(3, context.Votes.Count());
        Assert.True(controller.TempData.ContainsKey("Error"));
    }

    [Fact]
    public async Task F03_AdminDelete_EndedProgramWithoutVotes_IsDeleted()
    {
        using var context = OwnerTestKit.CreateContext();
        await OwnerTestKit.SeedAsync(context, OwnerTestKit.Ended(1, "owner-1"));
        var controller = CreateAdminController(context);

        await controller.Delete(1);

        Assert.Empty(context.VotingPrograms);
    }

    // ── F04: published link uses the slug ───────────────────────────────────

    private static (ProgramOwnerController controller, List<UrlActionContext> calls) ControllerCapturingUrls(
        Data.VotingDbContext context, IVotingService service)
    {
        var controller = OwnerTestKit.CreateController(context, service);
        var calls = new List<UrlActionContext>();
        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.Action(It.IsAny<UrlActionContext>()))
            .Callback<UrlActionContext>(calls.Add)
            .Returns("http://test/link");
        controller.Url = url.Object;
        return (controller, calls);
    }

    [Fact]
    public async Task F04_CreatePublishedWithSlug_LinkUsesSlugRoute()
    {
        using var context = OwnerTestKit.CreateContext();
        var program = WithSlug(OwnerTestKit.Upcoming(0, "owner-1"), "my-election");
        program.IsPublished = true;
        var service = new Mock<IVotingService>();
        service.Setup(s => s.ValidateAndCreateProgramAsync(It.IsAny<VotingProgram>())).ReturnsAsync((true, null));
        var (controller, calls) = ControllerCapturingUrls(context, service.Object);

        await controller.Create(program);

        var call = Assert.Single(calls);
        Assert.Equal("BySlug", call.Action);
        Assert.Equal("my-election", new RouteValueDictionaryView(call.Values)["slug"]);
    }

    [Fact]
    public async Task F04_CreatePublishedWithoutSlug_LinkUsesVoteId()
    {
        using var context = OwnerTestKit.CreateContext();
        var program = OwnerTestKit.Upcoming(5, "owner-1");
        program.IsPublished = true;
        var service = new Mock<IVotingService>();
        service.Setup(s => s.ValidateAndCreateProgramAsync(It.IsAny<VotingProgram>())).ReturnsAsync((true, null));
        var (controller, calls) = ControllerCapturingUrls(context, service.Object);

        await controller.Create(program);

        var call = Assert.Single(calls);
        Assert.Equal("Vote", call.Action);
        Assert.Equal(5, new RouteValueDictionaryView(call.Values)["id"]);
    }

    /// <summary>Reads anonymous-object route values (new { id = 5 }) by name.</summary>
    private sealed class RouteValueDictionaryView(object? values)
    {
        private readonly Microsoft.AspNetCore.Routing.RouteValueDictionary _values = new(values);
        public object? this[string key] => _values[key];
    }
}
