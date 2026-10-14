using Microsoft.AspNetCore.Html;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    /// <summary>View model for the _AuthCard partial shared by the small account pages.</summary>
    public sealed class AuthCardModel
    {
        public string Title { get; init; } = "";
        public string? Subtitle { get; init; }
        public string Icon { get; init; } = "bi-envelope-check";
        /// <summary>"", "is-success" or "is-danger".</summary>
        public string IconClass { get; init; } = "";
        public IHtmlContent? Body { get; init; }
    }
}
