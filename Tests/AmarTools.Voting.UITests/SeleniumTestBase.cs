using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using NUnit.Framework;

namespace AmarTools.Voting.UITests
{
    /// <summary>
    /// Shared Selenium fixture. Assumes the app is already running locally
    /// (e.g. `dotnet run`) at the URL configured below, and that seed users
    /// have been created (see appsettings.Development.json Seed:AdminEmail /
    /// Seed:AdminPassword, plus a manually-created "voter1@test.local" account
    /// with the password below for voter-flow tests).
    /// </summary>
    public abstract class SeleniumTestBase
    {
        protected IWebDriver Driver { get; private set; } = null!;
        protected WebDriverWait Wait { get; private set; } = null!;
        protected const string BaseUrl = "https://localhost:5001";

        [SetUp]
        public void SetUp()
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--ignore-certificate-errors"); // local dev cert
            options.AddArgument("--window-size=1400,1000");

            Driver = new ChromeDriver(options);
            Wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(10));
        }

        [TearDown]
        public void TearDown()
        {
            Driver?.Quit();
            Driver?.Dispose();
        }

        protected void LoginAs(string email, string password)
        {
            Driver.Navigate().GoToUrl($"{BaseUrl}/Identity/Account/Login");
            Wait.Until(d => d.FindElement(By.Id("Input_Email")));
            Driver.FindElement(By.Id("Input_Email")).SendKeys(email);
            Driver.FindElement(By.Id("Input_Password")).SendKeys(password);
            Driver.FindElement(By.CssSelector("button[type='submit']")).Click();
            Wait.Until(d => !d.Url.Contains("/Identity/Account/Login"));
        }
    }
}
