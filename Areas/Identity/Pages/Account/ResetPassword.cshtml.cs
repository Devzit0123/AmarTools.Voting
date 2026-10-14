using System.ComponentModel.DataAnnotations;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    [EnableRateLimiting("account")]
    public class ResetPasswordModel(UserManager<ApplicationUser> userManager) : PageModel
    {
        [BindProperty]
        public InputModel Input { get; set; } = new();

        public bool Succeeded { get; private set; }
        public bool InvalidLink { get; private set; }

        public class InputModel
        {
            [Required, EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Password is required.")]
            [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
            [DataType(DataType.Password)]
            [Display(Name = "New Password")]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Display(Name = "Confirm Password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; } = string.Empty;

            [Required]
            public string Code { get; set; } = string.Empty;
        }

        public void OnGet(string? code, string? email)
        {
            if (string.IsNullOrEmpty(code))
            {
                InvalidLink = true;
                return;
            }
            Input = new InputModel { Code = code, Email = email ?? string.Empty };
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var token = AccountEmails.DecodeToken(Input.Code);
            var user = await userManager.FindByEmailAsync(Input.Email.Trim());
            if (user is null || token is null)
            {
                // Do not reveal whether the account exists.
                Succeeded = true;
                return Page();
            }

            var result = await userManager.ResetPasswordAsync(user, token, Input.Password);
            if (result.Succeeded)
            {
                Succeeded = true;
                return Page();
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Code == "InvalidToken"
                    ? "This reset link is invalid or has expired. Please request a new one."
                    : error.Description);
            return Page();
        }
    }
}
