using System.ComponentModel.DataAnnotations;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    [EnableRateLimiting("account")]
    public class ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender) : PageModel
    {
        [BindProperty]
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid Email Address.")]
        public string Email { get; set; } = string.Empty;

        public bool Sent { get; private set; }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = await userManager.FindByEmailAsync(Email.Trim());
            // Only confirmed accounts can reset; an unconfirmed address was never proven
            // to belong to the person asking.
            if (user is not null && await userManager.IsEmailConfirmedAsync(user))
                await AccountEmails.SendPasswordResetAsync(userManager, emailSender, Url, Request.Scheme, user);

            // Same response either way, so the form does not reveal which emails exist.
            Sent = true;
            return Page();
        }
    }
}
