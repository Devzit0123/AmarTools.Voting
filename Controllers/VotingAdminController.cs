using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AmarTools.Voting.Controllers
{
    [Authorize(Roles = "Admin")]
    public class VotingAdminController(
        VotingDbContext context,
        IBlockchainService blockchainService,
        IVotingService votingService,
        UserManager<ApplicationUser> userManager) : Controller
    {
        private readonly VotingDbContext          _context           = context;
        private readonly IBlockchainService       _blockchainService = blockchainService;
        private readonly IVotingService           _votingService     = votingService;
        private readonly UserManager<ApplicationUser> _userManager   = userManager;

        
        private static DateTime ToLocal(DateTime utc) =>
            DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

        // ── Dashboard ──────────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var programs = await _votingService.GetAllProgramsForDashboardAsync();

            ViewBag.LocalNow         = ToLocal(DateTime.UtcNow);
            ViewBag.ProgramStartTimes = programs.ToDictionary(p => p.Id, p => ToLocal(p.StartTime));
            ViewBag.ProgramEndTimes   = programs.ToDictionary(p => p.Id, p => ToLocal(p.EndTime));

            return View(programs);
        }

        // ── Create Program ─────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Create()
        {
            var localNow = ToLocal(DateTime.UtcNow);
            var model    = new VotingProgram
            {
                StartTime = localNow,
                EndTime   = localNow.AddHours(1)
            };
            return View("CreateProgram", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> Create(VotingProgram model)
        {
           
            PrepareProgramModelForValidation();

            if (!ModelState.IsValid)
                return View("CreateProgram", model);

            var (success, errorMessage) = await _votingService.ValidateAndCreateProgramAsync(model);

            if (!success)
            {
                if (errorMessage != null)
                    ModelState.AddModelError(string.Empty, errorMessage);
                return View("CreateProgram", model);
            }

            if (model.IsPublished)
                TempData["PublishedLink"] = Url.Action("Vote", "Voting", new { id = model.Id }, Request.Scheme);

            TempData["Success"] = "Voting program created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ── Edit Program ───────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
           
            var program = await _context.VotingPrograms
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);

            if (program is null) return NotFound();

            program.StartTime = ToLocal(program.StartTime);
            program.EndTime   = ToLocal(program.EndTime);

            return View("CreateProgram", program);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> Edit(int id, VotingProgram model)
        {
            if (id != model.Id) return NotFound();

           
            PrepareProgramModelForValidation();

            if (!ModelState.IsValid)
                return View("CreateProgram", model);

            var (success, errorMessage) = await _votingService.ValidateAndUpdateProgramAsync(id, model);

            if (!success)
            {
                if (errorMessage != null)
                    ModelState.AddModelError(string.Empty, errorMessage);
                return View("CreateProgram", model);
            }

            if (model.IsPublished)
                TempData["PublishedLink"] = Url.Action("Vote", "Voting", new { id = model.Id }, Request.Scheme);

            TempData["Success"] = "Program updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        
        [HttpGet]
        public async Task<IActionResult> ManageCandidates(int programId)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(programId);
            if (program is null) return NotFound();

            ViewBag.Program       = program;
            ViewBag.StartTimeLocal = ToLocal(program.StartTime);
            ViewBag.EndTimeLocal   = ToLocal(program.EndTime);

            return View(program.Candidates);
        }

        
        [HttpGet]
        public async Task<IActionResult> Results(int id)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(id);
            if (program is null) return NotFound();

            var results    = await _votingService.GetResultsAsync(id);
            var totalVotes = results.Sum(r => r.VoteCount);

            ViewBag.Results         = results;
            ViewBag.TotalVotes      = totalVotes;
            ViewBag.BlockchainValid = await _blockchainService.IsChainValidForProgramAsync(_context, id);
            ViewBag.StartTimeLocal  = ToLocal(program.StartTime);
            ViewBag.EndTimeLocal    = ToLocal(program.EndTime);
            ViewBag.NowLocal        = ToLocal(DateTime.UtcNow);

            return View(program);
        }

        // ── Manage Voters ──────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ManageVoters(int programId)
        {
            var program = await _context.VotingPrograms
                .Include(p => p.Owner)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == programId);

            if (program == null) return NotFound();

            var voters = await _context.Voters
                .Where(v => v.ProgramId == programId)
                .OrderBy(v => v.Name)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Program = program;
            return View(voters);
        }

        // ── Register Voter ─────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> RegisterVoter(int programId)
        {
            var program = await _context.VotingPrograms.FindAsync(programId);
            if (program == null) return NotFound();

            ViewBag.ProgramId = programId;
            ViewBag.Program   = program;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> RegisterVoter(int programId, string name, string email, string? memberId)
        {
            
            var (success, error) = await _votingService.RegisterVoterByEmailAsync(
                programId, name, email, memberId, registrationSource: "admin");

            if (!success)
            {
                // Distinguish not-found from validation errors
                if (error == "Voting program not found.")
                    return NotFound();

                TempData["Error"] = error;
                return RedirectToAction(nameof(ManageVoters), new { programId });
            }

            TempData["Success"] = $"Voter '{name?.Trim()}' ({email?.Trim()}) registered successfully.";
            return RedirectToAction(nameof(ManageVoters), new { programId });
        }

        
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> RemoveVoter(int voterId, int programId)
        {
           
            var (success, error) = await _votingService.RemoveVoterAsync(voterId, programId);

            if (!success)
            {
                TempData["Error"] = error;
            }
            else
            {
                TempData["Success"] = "Voter removed successfully.";
            }

            return RedirectToAction(nameof(ManageVoters), new { programId });
        }

        
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var program = await _context.VotingPrograms.FindAsync(id);
            if (program is null) return NotFound();

            if (program.IsPublished && !program.HasEnded)
            {
                TempData["Error"] = "Cannot delete a published program that has not yet ended. Unpublish it first.";
                return RedirectToAction(nameof(Index));
            }

            _context.VotingPrograms.Remove(program);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Program deleted.";
            return RedirectToAction(nameof(Index));
        }

        
        private void PrepareProgramModelForValidation()
        {
            ModelState.Remove(nameof(VotingProgram.OwnerId));
            ModelState.Remove(nameof(VotingProgram.Owner));
            ModelState.Remove(nameof(VotingProgram.CreatedAt));
            ModelState.Remove(nameof(VotingProgram.CreatedBy));
            ModelState.Remove(nameof(VotingProgram.Candidates));
            ModelState.Remove(nameof(VotingProgram.Voters));
            ModelState.Remove(nameof(VotingProgram.Votes));
        }
    }
}
