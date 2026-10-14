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

            // ── Program Owners go to their dashboard ───────────────────────────────
            if (User.IsInRole("ProgramOwner"))
            {
                return RedirectToAction("MyPrograms", "ProgramOwner");
            }

            // ── Voters (signed in, no role) see the programs they are registered in ─
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = _userManager.GetUserId(User);
                var registrations = await _context.Voters
                    .Where(v => v.UserId == userId)
                    .Include(v => v.VotingProgram)
                    .Where(v => v.VotingProgram.IsPublished)
                    .OrderByDescending(v => v.VotingProgram.StartTime)
                    .AsNoTracking()
                    .ToListAsync();
                return View("MyElections", registrations);
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