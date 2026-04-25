using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly VotingDbContext _context;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            VotingDbContext context)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ReturnUrl { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Email is required")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Password is required")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me")]
            public bool RememberMe { get; set; }
        }

        public void OnGet(string? returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            ReturnUrl = ResolveDefaultReturnUrl(returnUrl);
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            // Keep original returnUrl for role-based redirect logic
            ReturnUrl = ResolveDefaultReturnUrl(returnUrl);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var email = Input.Email.Trim();
            var user = await _userManager.FindByEmailAsync(email);

            if (user == null || string.IsNullOrWhiteSpace(user.UserName))
            {
                ModelState.AddModelError(string.Empty, "Invalid email or password. Please try again.");
                return Page();
            }

            var result = await _signInManager.PasswordSignInAsync(
                user.UserName,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                await LinkExistingVoterRegistrationsAsync(user);

                // ✅ FIXED: Pass the ORIGINAL returnUrl (not the resolved ReturnUrl)
                // This allows the role check to execute for Admins
                var destination = await ResolveSuccessfulLoginRedirectAsync(user, returnUrl);
                return LocalRedirect(destination);
            }

            if (result.RequiresTwoFactor)
            {
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
            }

            if (result.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }

            ModelState.AddModelError(string.Empty, "Invalid email or password. Please try again.");
            return Page();
        }

        private async Task LinkExistingVoterRegistrationsAsync(ApplicationUser user)
        {
            var normalizedEmail = user.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return;
            }

            var pendingVoters = await _context.Voters
                .Where(v => v.Email != null && v.Email.ToLower() == normalizedEmail && v.UserId == null)
                .ToListAsync();

            foreach (var voter in pendingVoters)
            {
                voter.UserId = user.Id;
            }

            if (pendingVoters.Count > 0)
            {
                await _context.SaveChangesAsync();
            }
        }

        private string ResolveDefaultReturnUrl(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return returnUrl;
            }

            return Url.Content("~/ProgramOwner/MyPrograms");
        }

        private async Task<string> ResolveSuccessfulLoginRedirectAsync(ApplicationUser user, string? originalReturnUrl)
        {
            // If a specific returnUrl was provided and it's local, use it first (highest priority)
            if (!string.IsNullOrWhiteSpace(originalReturnUrl) && Url.IsLocalUrl(originalReturnUrl))
            {
                return originalReturnUrl;
            }

            // Role-based redirect
            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                return Url.Content("~/VotingAdmin/Index");
            }

            // Default for ProgramOwner (and any other roles)
            return Url.Content("~/ProgramOwner/MyPrograms");
        }
    }
}