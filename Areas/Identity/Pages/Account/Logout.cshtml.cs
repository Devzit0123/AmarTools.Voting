using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    public class LogoutModel(SignInManager<ApplicationUser> signInManager) : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager = signInManager;

        public async Task<IActionResult> OnPostAsync()
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage("/Account/Login");
        }
    }
}