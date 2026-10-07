using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AmarTools.Voting.Models;
using AmarTools.Voting.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AmarTools.Voting.Controllers
{
    public class HomeController : Controller
    {
        private readonly VotingDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(VotingDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            // ── Priority 1: Redirect Admins to Admin Dashboard ─────────────────────
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "VotingAdmin");
            }

            // ── Program Owners (and other authenticated users) go to their
            //    one real dashboard — no separate duplicate view to keep in sync. ──
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("MyPrograms", "ProgramOwner");
            }

            // ── Not logged in → Show Landing Page ─────────────────────────────────
            return View(new List<VotingProgram>());
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}