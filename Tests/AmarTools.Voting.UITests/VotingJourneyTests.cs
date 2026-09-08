using OpenQA.Selenium;
using NUnit.Framework;

namespace AmarTools.Voting.UITests
{
    /// <summary>
    /// Black-box automation of the M1 CastVote decision table (see
    /// ECT_DecisionTable tab, rules R1-R7) and the Join -> Vote -> ThankYou journey,
    /// driven against the real rendered Vote.cshtml markup:
    ///   &lt;form asp-action="CastVote"&gt; per-candidate, hidden programId/candidateId
    ///   inputs, and a JS confirm() dialog on submit.
    ///
    /// Prerequisites (fill in for your environment before running):
    ///   - App running locally at BaseUrl (dotnet run)
    ///   - A published, currently-active VotingProgram with >=1 candidate exists
    ///   - A seeded voter account "voter1@test.local" / "P@ssword1" exists and is
    ///     registered (Joined) for that program
    /// </summary>
    [TestFixture]
    public class VotingJourneyTests : SeleniumTestBase
    {
        // TODO: replace with a real seeded program id / candidate name before running
        private const string ActiveProgramId = "1";
        private const string CandidateName = "Alice Rahman";

        [Test]
        public void Voter_CanCastVote_ExactlyOnce_ThenIsBlockedOnSecondAttempt()
        {
            LoginAs("voter1@test.local", "P@ssword1");
            Driver.Navigate().GoToUrl($"{BaseUrl}/Voting/Vote/{ActiveProgramId}");

            // Accept the "Vote for X? This cannot be undone." confirm() dialog before it blocks.
            Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(10);
            var voteButton = Wait.Until(d =>
                d.FindElement(By.XPath($"//h5[text()='{CandidateName}']/ancestor::div[contains(@class,'card')]//button[@type='submit']")));
            voteButton.Click();

            Driver.SwitchTo().Alert().Accept(); // confirm() dialog

            Wait.Until(d => d.Url.Contains("/Voting/ThankYou"));
            Assert.That(Driver.Url, Does.Contain("/Voting/ThankYou"));
            Assert.That(Driver.PageSource, Does.Contain("recorded"));

            // Act 2 — return to the Vote page and confirm the option to vote again is gone.
            Driver.Navigate().GoToUrl($"{BaseUrl}/Voting/Vote/{ActiveProgramId}");

            Assert.That(Driver.PageSource, Does.Not.Contain("Vote for " + CandidateName));
        }

        [Test]
        public void UnregisteredAuthenticatedUser_SeesDisabledVoteButtons_NotForm()
        {
            // A logged-in user who never Joined the program should see disabled
            // buttons rather than an active CastVote form (Vote.cshtml else-branch).
            LoginAs("voter2-never-joined@test.local", "P@ssword1");
            Driver.Navigate().GoToUrl($"{BaseUrl}/Voting/Vote/{ActiveProgramId}");

            var disabledButtons = Driver.FindElements(By.CssSelector("button.btn-outline-secondary[disabled]"));
            Assert.That(disabledButtons.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ClosedProgram_ShowsClosedView_NotVoteForm()
        {
            // Requires a seeded, ended/unpublished program id substituted below.
            const string endedProgramId = "999"; // TODO: replace with a real ended program id
            Driver.Navigate().GoToUrl($"{BaseUrl}/Voting/Vote/{endedProgramId}");

            Assert.That(Driver.PageSource, Does.Contain("not currently active").IgnoreCase
                .Or.Contain("This voting program is no longer").IgnoreCase);
        }

        [Test]
        public void SearchBox_ReturnsAtMostEightResults_ForBroadQuery()
        {
            // Regression test for VotingController.Search's .Take(8) boundary
            // (BVA tab rows 007-012).
            Driver.Navigate().GoToUrl(BaseUrl);
            var searchBox = Wait.Until(d => d.FindElement(By.Id("program-search")));
            searchBox.SendKeys("e"); // broad single-letter query, likely to match many programs

            Wait.Until(d => d.FindElements(By.CssSelector(".search-result-item")).Count > 0
                            || d.FindElements(By.CssSelector(".search-result-item")).Count == 0);

            var results = Driver.FindElements(By.CssSelector(".search-result-item"));
            Assert.That(results.Count, Is.LessThanOrEqualTo(8));
        }
    }
}
