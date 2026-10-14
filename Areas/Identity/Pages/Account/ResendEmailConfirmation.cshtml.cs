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
    public class ResendEmailConfirmationModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender) : PageModel
    {
        [BindProperty]
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid Email Address.")]
        public string Email { get; set; } = string.Empty;

        public bool Sent { get; private set; }

        public void OnGet(string? email) => Email = email ?? string.Empty;

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = await userManager.FindByEmailAsync(Email.Trim());
            if (user is not null && !user.EmailConfirmed)
                await AccountEmails.SendConfirmationAsync(userManager, emailSender, Url, Request.Scheme, user);

            // Same message whether or not the account exists, so the form cannot be used
            // to find out which emails are registered.
            Sent = true;
            return Page();
        }
    }
}
