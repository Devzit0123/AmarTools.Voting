using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Xunit;

namespace AmarTools.Voting.Tests;

public sealed class ProgramOwnerSeleniumTests
{
    [Fact]
    public void ProgramOwnerDashboard_IsAvailableToConfiguredOwner()
    {
        using var driver = LoginAsConfiguredOwner(out _);
        if (driver is null) return;

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        wait.Until(d => d.Url.Contains("/ProgramOwner/MyPrograms", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("My Programs", driver.PageSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramOwner_CreatePage_IsAvailableToConfiguredOwner()
    {
        using var driver = LoginAsConfiguredOwner(out var baseUrl);
        if (driver is null) return;

        driver.Navigate().GoToUrl(new Uri(new Uri(baseUrl!), "/ProgramOwner/Create"));

        Assert.Contains("Create", driver.PageSource, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(driver.FindElement(By.Name("ProgramName")));
        Assert.NotNull(driver.FindElement(By.Name("StartTime")));
        Assert.NotNull(driver.FindElement(By.Name("EndTime")));
    }

    private static IWebDriver? LoginAsConfiguredOwner(out string? baseUrl)
    {
        baseUrl = Environment.GetEnvironmentVariable("AMARTOOLS_BASE_URL");
        var email = Environment.GetEnvironmentVariable("AMARTOOLS_TEST_EMAIL");
        var password = Environment.GetEnvironmentVariable("AMARTOOLS_TEST_PASSWORD");

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
            return null;

        var driver = CreateDriver();
        driver.Navigate().GoToUrl(new Uri(new Uri(baseUrl), "/Identity/Account/Login"));
        driver.FindElement(By.Name("Input.Email")).SendKeys(email);
        driver.FindElement(By.Name("Input.Password")).SendKeys(password);
        driver.FindElement(By.CssSelector("button[type='submit']")).Click();
        return driver;
    }

    private static IWebDriver CreateDriver()
    {
        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        return new ChromeDriver(options);
    }
}
