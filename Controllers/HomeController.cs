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

        private static DateTime ToLocal(DateTime utc) =>
            DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

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

            // ── For Program Owners (and other authenticated users) ─────────────────
            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    var programs = await _context.VotingPrograms
                        .Include(p => p.Candidates)
                        .Include(p => p.Votes)
                        .Include(p => p.Voters)
                        .Where(p => p.OwnerId == user.Id)
                        .OrderByDescending(p => p.CreatedAt)
                        .ToListAsync();

                    // Dictionaries required by the view for time formatting
                    ViewBag.ProgramStartTimes = programs.ToDictionary(p => p.Id, p => ToLocal(p.StartTime));
                    ViewBag.ProgramEndTimes = programs.ToDictionary(p => p.Id, p => ToLocal(p.EndTime));

                    return View(programs);
                }
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
