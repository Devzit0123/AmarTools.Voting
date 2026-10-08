using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace AmarTools.Voting.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  Selenium end-to-end tests for the ProgramOwner area.
//
//  Run against a TEST database (these tests create programs, candidates, voters).
//
//    $env:AMARTOOLS_BASE_URL     = "https://localhost:5001"
//    $env:AMARTOOLS_TEST_EMAIL   = "owner@example.com"      # a ProgramOwner or Admin account
//    $env:AMARTOOLS_TEST_PASSWORD= "..."
//    dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "FullyQualifiedName~ProgramOwnerSeleniumFlowTests"
//
//  If any variable is missing the tests are reported as SKIPPED (not passed).
//  Known-defect tests carry Category=Defect; they assert the correct behaviour
//  and are expected to fail until the defect is fixed.
//
//  Selector assumptions (taken from the Razor views): #programForm on the
//  create/edit page; form[action*='AddCandidate'] with inputs name / candidateCode /
//  description / imageUrl; RegisterVoter form with name / email / memberId; the
//  newest program is the first row on MyPrograms (ordered by CreatedAt desc).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SeleniumFactAttribute : FactAttribute
{
    public SeleniumFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMARTOOLS_BASE_URL")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMARTOOLS_TEST_EMAIL")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMARTOOLS_TEST_PASSWORD")))
        {
            Skip = "Set AMARTOOLS_BASE_URL, AMARTOOLS_TEST_EMAIL and AMARTOOLS_TEST_PASSWORD to run Selenium tests.";
        }
    }
}

public sealed class OwnerBrowserFixture : IDisposable
{
    public IWebDriver? Driver { get; }
    public string BaseUrl { get; } = "";

    public OwnerBrowserFixture()
    {
        var baseUrl = Environment.GetEnvironmentVariable("AMARTOOLS_BASE_URL");
        var email = Environment.GetEnvironmentVariable("AMARTOOLS_TEST_EMAIL");
        var password = Environment.GetEnvironmentVariable("AMARTOOLS_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
            return;

        BaseUrl = baseUrl.TrimEnd('/');

        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--ignore-certificate-errors");
        options.AddArgument("--window-size=1400,1000");
        Driver = new ChromeDriver(options);
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;

        Driver.Navigate().GoToUrl($"{BaseUrl}/Identity/Account/Login");
        Driver.FindElement(By.Name("Input.Email")).SendKeys(email);
        Driver.FindElement(By.Name("Input.Password")).SendKeys(password);
        Driver.FindElement(By.CssSelector("button[type='submit']")).Click();
        Wait(d => !d.Url.Contains("/Login", StringComparison.OrdinalIgnoreCase));
    }

    public void Wait(Func<IWebDriver, bool> condition, int seconds = 10) =>
        new WebDriverWait(Driver!, TimeSpan.FromSeconds(seconds)).Until(condition);

    public void Open(string path) => Driver!.Navigate().GoToUrl($"{BaseUrl}{path}");

    public void SetInput(string cssSelector, string value)
    {
        var element = Driver!.FindElement(By.CssSelector(cssSelector));
        element.Clear();
        element.SendKeys(value);
    }

    public void SetDateTimeLocal(string name, DateTime value)
    {
        var driver = Driver!;
        var js = (IJavaScriptExecutor)driver;
        var element = driver.FindElement(By.Name(name));

        var formatted = value.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

        js.ExecuteScript(
            "arguments[0].value = arguments[1];" +
            "arguments[0].dispatchEvent(new Event('input', { bubbles: true }));" +
            "arguments[0].dispatchEvent(new Event('change', { bubbles: true }));",
            element, formatted);

        // Keep the hidden browser-timezone fields in sync. The page's submit handler
        // normally writes -getTimezoneOffset() (= local - UTC, positive east of UTC) and
        // VotingService.NormalizeToUtc computes UTC = local - offset, so the value here is
        // the local UTC offset as-is (no extra negation).
        var offsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(value).TotalMinutes;
        var offsetFieldName = name == "StartTime" ? "StartTimeOffsetMinutes" : "EndTimeOffsetMinutes";
        var offsetField = driver.FindElement(By.Name(offsetFieldName));

        js.ExecuteScript(
            "arguments[0].value = arguments[1];",
            offsetField,
            offsetMinutes.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Waits for a JavaScript alert (e.g. the client-side 5-minute rule) and returns it.</summary>
    public IAlert WaitForAlert(int seconds = 10) =>
        new WebDriverWait(Driver!, TimeSpan.FromSeconds(seconds))
        {
            PollingInterval = TimeSpan.FromMilliseconds(200)
        }.Until(d =>
        {
            try { return d.SwitchTo().Alert(); }
            catch (NoAlertPresentException) { return null; }
        })!;

    public string PageText => Driver!.PageSource;

    /// <summary>Creates a program through the UI and returns its id (newest row on MyPrograms).</summary>
    public int CreateProgram(string name, int startInMinutes = 60, int durationMinutes = 120, bool publish = false)
    {
        Open("/ProgramOwner/Create");
        Wait(d => d.FindElements(By.Name("ProgramName")).Count > 0);

        SetInput("[name='ProgramName']", name);
        SetDateTimeLocal("StartTime", DateTime.Now.AddMinutes(startInMinutes));
        SetDateTimeLocal("EndTime", DateTime.Now.AddMinutes(startInMinutes + durationMinutes));
        if (publish)
        {
            var box = Driver!.FindElements(By.CssSelector("#programForm input[type='checkbox'][name='IsPublished']"))
                .FirstOrDefault();
            if (box is { Selected: false }) box.Click();
        }

        Driver!.FindElement(By.CssSelector("#programForm [type='submit']")).Click();

        try
        {
            Wait(d => d.Url.Contains("/ProgramOwner/MyPrograms", StringComparison.OrdinalIgnoreCase));
        }
        catch (WebDriverTimeoutException)
        {
            throw new Exception(
                "Program creation did not redirect to MyPrograms.\n" +
                $"Current URL: {Driver.Url}\n" +
                $"Page text: {Driver.PageSource}");
        }

        var href = Driver.FindElement(By.CssSelector("a[href*='ManageCandidates']")).GetDomAttribute("href")!;
        return int.Parse(Regex.Match(href, @"programId=(\d+)").Groups[1].Value);
    }

    public void AddCandidate(int programId, string name, string code, string? imageUrl = null, string? description = null)
    {
        Open($"/ProgramOwner/ManageCandidates?programId={programId}");
        Wait(d => d.FindElements(By.CssSelector("form[action*='AddCandidate']")).Count > 0);
        const string form = "form[action*='AddCandidate'] ";
        SetInput(form + "input[name='name']", name);
        SetInput(form + "input[name='candidateCode']", code);
        if (description is not null) SetInput(form + "textarea[name='description']", description);
        if (imageUrl is not null) SetInput(form + "input[name='imageUrl']", imageUrl);
        var formElement = Driver!.FindElement(By.CssSelector(form.Trim()));
        Driver.FindElement(By.CssSelector(form + "button[type='submit']")).Click();

        // The page is already on ManageCandidates before the click, so the URL alone cannot
        // tell us the POST-redirect-GET finished. Wait for the old form to go stale (the page
        // was replaced), then for the reloaded page to contain the form again.
        Wait(d =>
        {
            try { _ = formElement.Enabled; return false; }
            catch (StaleElementReferenceException) { return true; }
        });
        Wait(d => d.Url.Contains("ManageCandidates", StringComparison.OrdinalIgnoreCase) &&
                  d.FindElements(By.CssSelector(form.Trim())).Count > 0);
    }

    public void RegisterVoter(int programId, string name, string email)
    {
        Open($"/ProgramOwner/RegisterVoter?programId={programId}");
        Wait(d => d.FindElements(By.CssSelector("input[name='email']")).Count > 0);
        SetInput("input[name='name']", name);
        SetInput("input[name='email']", email);
        Driver!.FindElement(By.CssSelector("form[action*='RegisterVoter'] button[type='submit']")).Click();
        Wait(d => d.Url.Contains("ManageVoters", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => Driver?.Quit();
}

public sealed class ProgramOwnerSeleniumFlowTests : IClassFixture<OwnerBrowserFixture>
{
    private readonly OwnerBrowserFixture _ui;

    public ProgramOwnerSeleniumFlowTests(OwnerBrowserFixture ui) => _ui = ui;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    // S01
    [SeleniumFact]
    public void Dashboard_IsShownAfterLogin()
    {
        _ui.Open("/ProgramOwner/MyPrograms");
        Assert.Contains("ProgramOwner/MyPrograms", _ui.Driver!.Url, StringComparison.OrdinalIgnoreCase);
    }

    // S02
    [SeleniumFact]
    public void CreateProgram_ValidInput_AppearsOnDashboard()
    {
        var name = Unique("Election");

        _ui.CreateProgram(name);

        Assert.Contains(name, _ui.PageText);
        Assert.Contains("created successfully", _ui.PageText, StringComparison.OrdinalIgnoreCase);
    }

    // S03 — D06: times must not shift after a validation error
    [SeleniumFact]
    [Trait("Category", "Defect")]
    public void CreateProgram_DurationUnderFiveMinutes_KeepsTheTimesTheUserTyped()
    {
        _ui.Open("/ProgramOwner/Create");
        _ui.Wait(d => d.FindElements(By.Name("ProgramName")).Count > 0);
        var start = DateTime.Now.AddMinutes(60);
        var end = start.AddMinutes(2);
        _ui.SetInput("[name='ProgramName']", Unique("TooShort"));
        _ui.SetDateTimeLocal("StartTime", start);
        _ui.SetDateTimeLocal("EndTime", end);

        _ui.Driver!.FindElement(By.CssSelector("#programForm [type='submit']")).Click();

        // The 5-minute rule is enforced by a JavaScript alert, not by text in the page.
        var alert = _ui.WaitForAlert();
        Assert.Contains("at least 5 minutes", alert.Text, StringComparison.OrdinalIgnoreCase);
        alert.Accept();

        var shown = _ui.Driver.FindElement(By.Name("StartTime")).GetDomProperty("value");
        Assert.Equal(start.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), shown);
    }

    // S04
    [SeleniumFact]
    public void AddCandidate_ValidInput_ShowsSuccess()
    {
        var id = _ui.CreateProgram(Unique("Cand"));

        _ui.AddCandidate(id, "Alice Rahman", "A1");

        Assert.Contains("added successfully", _ui.PageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alice Rahman", _ui.PageText);
    }

    // S05
    [SeleniumFact]
    public void AddCandidate_DuplicateCode_ShowsError()
    {
        var id = _ui.CreateProgram(Unique("Dup"));
        _ui.AddCandidate(id, "Alice Rahman", "A1");

        _ui.AddCandidate(id, "Bob Karim", "A1");

        Assert.Contains("already exists", _ui.PageText, StringComparison.OrdinalIgnoreCase);
    }

    // S06
    [SeleniumFact]
    public void AddCandidate_JavascriptImageUrl_IsRejected()
    {
        var id = _ui.CreateProgram(Unique("Js"));

        _ui.AddCandidate(id, "Alice Rahman", "A1", imageUrl: "javascript:alert(1)");

        Assert.Contains("valid image URL", _ui.PageText, StringComparison.OrdinalIgnoreCase);
    }

    // S07 — D09
    [SeleniumFact]
    [Trait("Category", "Defect")]
    public void AddCandidate_ProtocolRelativeImageUrl_IsRejected()
    {
        var id = _ui.CreateProgram(Unique("Proto"));

        _ui.AddCandidate(id, "Alice Rahman", "A1", imageUrl: "//evil.example/photo.png");

        Assert.Contains("valid image URL", _ui.PageText, StringComparison.OrdinalIgnoreCase);
    }

    // S08 — D07
    [SeleniumFact]
    [Trait("Category", "Defect")]
    public void AddCandidate_SingleCharacterName_IsRejected()
    {
        var id = _ui.CreateProgram(Unique("OneChar"));

        _ui.AddCandidate(id, "A", "A1");

        Assert.DoesNotContain("added successfully", _ui.PageText, StringComparison.OrdinalIgnoreCase);
    }

    // S09
    [SeleniumFact]
    public void RegisterVoter_ValidInput_ShowsSuccess()
    {
        var id = _ui.CreateProgram(Unique("Voter"));

        _ui.RegisterVoter(id, "Nadia Islam", $"{Guid.NewGuid():N}@example.com");

        Assert.Contains("registered successfully", _ui.PageText, StringComparison.OrdinalIgnoreCase);
    }

    // S10
    [SeleniumFact]
    public void RegisterVoter_DuplicateEmail_ShowsError()
    {
        var id = _ui.CreateProgram(Unique("VoterDup"));
        var email = $"{Guid.NewGuid():N}@example.com";
        _ui.RegisterVoter(id, "Nadia Islam", email);

        _ui.RegisterVoter(id, "Nadia Again", email);

        Assert.Contains("already registered", _ui.PageText, StringComparison.OrdinalIgnoreCase);
    }

    // S11
    [SeleniumFact]
    public void DeleteProgram_Unpublished_RemovesItFromTheDashboard()
    {
        var name = Unique("Delete");
        var id = _ui.CreateProgram(name);

        _ui.Driver!.FindElement(By.CssSelector($"form[action*='/ProgramOwner/Delete/{id}'] button[type='submit']")).Click();
        _ui.Driver.SwitchTo().Alert().Accept();   // the page asks for confirmation
        _ui.Wait(d => d.PageSource.Contains("Program deleted", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(name, _ui.PageText);
    }
}