using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly VotingDbContext _context;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            VotingDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; } = string.Empty;

        public class InputModel
        {
            [Required(ErrorMessage = "Full Name is required.")]
            [Display(Name = "Full Name")]
            public string FullName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Email is required.")]
            [EmailAddress(ErrorMessage = "Invalid Email Address.")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Password is required.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Required(ErrorMessage = "Confirm Password is required.")]
            [DataType(DataType.Password)]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            [Display(Name = "Confirm Password")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl = ResolvePostAuthRedirect(returnUrl);
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = ResolvePostAuthRedirect(returnUrl);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var email = Input.Email.Trim();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = Input.FullName.Trim()
            };

            var result = await _userManager.CreateAsync(user, Input.Password);

            if (result.Succeeded)
            {
                if (!await _roleManager.RoleExistsAsync("ProgramOwner"))
                {
                    await _roleManager.CreateAsync(new IdentityRole("ProgramOwner"));
                }

                await _userManager.AddToRoleAsync(user, "ProgramOwner");
                await LinkExistingVoterRegistrationsAsync(user);
                await _signInManager.SignInAsync(user, isPersistent: false);

                return LocalRedirect(ReturnUrl);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

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

        private string ResolvePostAuthRedirect(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return returnUrl;
            }

            return Url.Content("~/ProgramOwner/MyPrograms");
        }
    }
}