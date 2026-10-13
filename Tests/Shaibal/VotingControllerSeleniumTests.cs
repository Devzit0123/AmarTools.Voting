using System.Net;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  Selenium / end-to-end tests for VotingController (public voting pages).
//
//  Run the web app first (a TEST database!):   dotnet run --project AmarTools.Voting.csproj
//  Then, in another terminal (PowerShell):
//
//    $env:AMARTOOLS_BASE_URL          = "http://localhost:5256"
//    $env:AMARTOOLS_ACTIVE_PROGRAM_ID = "1"      # published program that is open right now, with >= 1 candidate
//    $env:AMARTOOLS_CLOSED_PROGRAM_ID = "2"      # program that has ended or has not started
//    $env:AMARTOOLS_TEST_EMAIL        = "voter@example.com"   # account that is a REGISTERED voter of the active program
//    $env:AMARTOOLS_TEST_PASSWORD     = "..."
//    $env:AMARTOOLS_ALLOW_VOTE        = "1"      # only for the flow that really casts a vote (once per voter!)
//    dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "FullyQualifiedName~VotingControllerSeleniumTests"
//
//  A test whose variables are missing is reported as SKIPPED, never as passed.
//  Selectors come from Views/Voting/Vote.cshtml and Areas/Identity/Pages/Account/Login.cshtml.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class VotingSeleniumFactAttribute : FactAttribute
{
    public VotingSeleniumFactAttribute(params string[] requiredVariables)
    {
        var required = new List<string> { "AMARTOOLS_BASE_URL" };
        required.AddRange(requiredVariables);

        var missing = required.Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))).ToList();
        if (missing.Count > 0)
            Skip = "Set environment variable(s): " + string.Join(", ", missing);
    }
}

public sealed class VotingBrowserFixture : IDisposable
{
    public string BaseUrl { get; } =
        (Environment.GetEnvironmentVariable("AMARTOOLS_BASE_URL") ?? "").TrimEnd('/');

    private IWebDriver? _driver;

    /// <summary>Lazily started so tests that only use HTTP never open Chrome.</summary>
    public IWebDriver Driver => _driver ??= CreateDriver();

    private static IWebDriver CreateDriver()
    {
        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--ignore-certificate-errors");
        options.AddArgument("--window-size=1400,1000");
        var driver = new ChromeDriver(options);
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
        return driver;
    }

    public void Open(string path) => Driver.Navigate().GoToUrl(BaseUrl + path);

    public void Wait(Func<IWebDriver, bool> condition, int seconds = 10) =>
        new WebDriverWait(Driver, TimeSpan.FromSeconds(seconds)).Until(condition);

    public void Login(string email, string password)
    {
        Open("/Identity/Account/Login");
        Driver.FindElement(By.Name("Input.Email")).SendKeys(email);
        Driver.FindElement(By.Name("Input.Password")).SendKeys(password);
        Driver.FindElement(By.CssSelector("form#account button[type='submit']")).Click();
        Wait(d => !d.Url.Contains("/Login", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<HttpStatusCode> GetStatusAsync(string path)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync(BaseUrl + path);
        return response.StatusCode;
    }

    public static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";

    public void Dispose() => _driver?.Quit();
}

public sealed class VotingControllerSeleniumTests : IClassFixture<VotingBrowserFixture>
{
    private readonly VotingBrowserFixture _site;

    public VotingControllerSeleniumTests(VotingBrowserFixture site) => _site = site;

    // SEL-01  Vote page of a program that does not exist -> HTTP 404
    [VotingSeleniumFact]
    public async Task SEL01_VotePage_UnknownProgram_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, await _site.GetStatusAsync("/Voting/Vote/99999999"));
    }

    // SEL-02  Public results of a program that does not exist -> HTTP 404
    [VotingSeleniumFact]
    public async Task SEL02_PublicResults_UnknownProgram_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, await _site.GetStatusAsync("/Voting/PublicResults/99999999"));
    }

    // SEL-03  A closed program shows the "Voting Not Available" page
    [VotingSeleniumFact("AMARTOOLS_CLOSED_PROGRAM_ID")]
    public void SEL03_VotePage_ClosedProgram_ShowsNotAvailablePage()
    {
        _site.Open("/Voting/Vote/" + VotingBrowserFixture.Env("AMARTOOLS_CLOSED_PROGRAM_ID"));

        Assert.Contains("Voting Not Available", _site.Driver.PageSource);
        Assert.Contains("not currently active", _site.Driver.PageSource);
    }

    // SEL-04  Anonymous visitor on an open program is asked to sign in
    [VotingSeleniumFact("AMARTOOLS_ACTIVE_PROGRAM_ID")]
    public void SEL04_VotePage_ActiveProgram_Anonymous_ShowsSignInPrompt()
    {
        _site.Open("/Voting/Vote/" + VotingBrowserFixture.Env("AMARTOOLS_ACTIVE_PROGRAM_ID"));

        Assert.Contains("Sign in to participate", _site.Driver.PageSource);
        Assert.NotEmpty(_site.Driver.FindElements(By.PartialLinkText("Sign in to Vote")));
    }

    // SEL-05  The search endpoint returns [] for an empty query
    [VotingSeleniumFact]
    public void SEL05_Search_EmptyQuery_ReturnsEmptyJsonArray()
    {
        _site.Open("/Voting/Search?q=");

        Assert.Equal("[]", _site.Driver.FindElement(By.TagName("body")).Text.Trim());
    }

    // SEL-06  The search endpoint answers a real query with a JSON array.
    //         PREDICTED TO FAIL (defect DV-01: HTTP 500 because IsActive cannot be translated).
    [VotingSeleniumFact]
    [Trait("Category", "Defect")]
    public async Task SEL06_Search_ValidQuery_Returns200()
    {
        Assert.Equal(HttpStatusCode.OK, await _site.GetStatusAsync("/Voting/Search?q=a"));
    }

    // SEL-07  Casting a vote needs a signed-in user: a direct POST without a session is refused
    //         (the [Authorize] filter redirects to the login page before the action runs).
    [VotingSeleniumFact]
    public async Task SEL07_CastVote_WithoutSignIn_IsNotAccepted()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["programId"] = "1",
            ["candidateId"] = "1",
        });

        using var response = await client.PostAsync(_site.BaseUrl + "/Voting/CastVote", form);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // SEL-08  Registered voter signs in, votes for the first candidate and lands on the thank-you page.
    //         Casts a REAL vote: each voter can run it once. Needs AMARTOOLS_ALLOW_VOTE=1.
    [VotingSeleniumFact("AMARTOOLS_ACTIVE_PROGRAM_ID", "AMARTOOLS_TEST_EMAIL", "AMARTOOLS_TEST_PASSWORD", "AMARTOOLS_ALLOW_VOTE")]
    public void SEL08_FullFlow_RegisteredVoter_CastsVote_ReachesThankYou()
    {
        _site.Login(VotingBrowserFixture.Env("AMARTOOLS_TEST_EMAIL"), VotingBrowserFixture.Env("AMARTOOLS_TEST_PASSWORD"));
        _site.Open("/Voting/Vote/" + VotingBrowserFixture.Env("AMARTOOLS_ACTIVE_PROGRAM_ID"));

        var voteButton = _site.Driver.FindElement(By.CssSelector("form[action*='CastVote'] button[type='submit']"));
        voteButton.Click();

        // The button runs confirm('Vote for ...? This cannot be undone.')
        _site.Wait(d => { try { d.SwitchTo().Alert(); return true; } catch (NoAlertPresentException) { return false; } });
        _site.Driver.SwitchTo().Alert().Accept();

        _site.Wait(d => d.Url.Contains("/Voting/ThankYou", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Thank You", _site.Driver.PageSource);
    }
}
