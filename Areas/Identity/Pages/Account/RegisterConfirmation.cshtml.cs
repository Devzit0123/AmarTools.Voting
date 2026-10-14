using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    /// <summary>"Check your inbox" page shown after registration.</summary>
    [AllowAnonymous]
    public class RegisterConfirmationModel : PageModel
    {
        public string? Email { get; private set; }

        public void OnGet(string? email) => Email = email;
    }
}
