using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    /// <summary>Target of the confirmation link. Confirms the email and links pre-registered voter slots (D15).</summary>
    [AllowAnonymous]
    public class ConfirmEmailModel(UserManager<ApplicationUser> userManager, VotingDbContext context) : PageModel
    {
        public bool Succeeded { get; private set; }
        public int LinkedVoterSlots { get; private set; }

        public async Task<IActionResult> OnGetAsync(string? userId, string? code)
        {
            var token = AccountEmails.DecodeToken(code);
            if (string.IsNullOrEmpty(userId) || token is null)
                return Page();

            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
                return Page();

            var result = await userManager.ConfirmEmailAsync(user, token);
            Succeeded = result.Succeeded;

            if (Succeeded)
                LinkedVoterSlots = await VoterLinking.LinkPendingRegistrationsAsync(context, user);

            return Page();
        }
    }
}
