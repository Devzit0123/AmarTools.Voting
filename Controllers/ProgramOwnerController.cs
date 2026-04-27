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
    [Authorize(Roles = "ProgramOwner, Admin")]
    public class ProgramOwnerController(
        VotingDbContext context,
        IVotingService votingService,
        IBlockchainService blockchainService,
        UserManager<ApplicationUser> userManager) : Controller
    {
        private const string CreateProgramView    = "~/Views/Shared/CreateProgram.cshtml";
        private const string ManageCandidatesView = "~/Views/VotingAdmin/ManageCandidates.cshtml";
        private const string ManageVotersView     = "~/Views/ProgramOwner/ManageVoters.cshtml";
        private const string ResultsView          = "~/Views/VotingAdmin/Results.cshtml";

        private readonly VotingDbContext             _context           = context;
        private readonly IVotingService              _votingService     = votingService;
        private readonly IBlockchainService          _blockchainService = blockchainService;
        private readonly UserManager<ApplicationUser> _userManager      = userManager;

        private static DateTime ToLocal(DateTime utc) =>
            DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

        private string? CurrentUserId =>
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // ── My Programs ────────────────────────────────────────────────────────
        public async Task<IActionResult> MyPrograms()
        {
            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            ViewBag.OwnerName = user?.FullName ?? user?.UserName ?? User.Identity?.Name ?? "there";

            var programs = await _context.VotingPrograms
                .Include(p => p.Candidates)
                .Include(p => p.Votes)
                .Include(p => p.Voters)
                .Where(p => p.OwnerId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .AsNoTracking()
                .ToListAsync();

            return View(programs);
        }

        // ── Create Program ─────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Create()
        {
            var localNow = ToLocal(DateTime.UtcNow);
            return View(CreateProgramView, new VotingProgram
            {
                StartTime = localNow,
                EndTime   = localNow.AddHours(1)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> Create(VotingProgram model)
        {
            PrepareProgramModelForValidation();

            if (!ModelState.IsValid)
                return View(CreateProgramView, model);

            var (success, errorMessage) = await _votingService.ValidateAndCreateProgramAsync(model);

            if (!success)
            {
                if (errorMessage != null)
                    ModelState.AddModelError(string.Empty, errorMessage);
                return View(CreateProgramView, model);
            }

            if (model.IsPublished)
                TempData["PublishedLink"] = Url.Action("Vote", "Voting", new { id = model.Id }, Request.Scheme);

            TempData["Success"] = "Voting program created successfully.";
            return RedirectToAction(nameof(MyPrograms));
        }

        // ── Edit Program ───────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // FIX: Use AsNoTracking to avoid tracked-entity mutation (same fix as Admin)
            var program = await _context.VotingPrograms
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);

            if (program is null) return NotFound();

            if (program.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
                return Forbid();

            program.StartTime = ToLocal(program.StartTime);
            program.EndTime   = ToLocal(program.EndTime);

            return View(CreateProgramView, program);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> Edit(int id, VotingProgram model)
        {
            if (id != model.Id) return NotFound();

            PrepareProgramModelForValidation();

            if (!ModelState.IsValid)
                return View(CreateProgramView, model);

            var (success, errorMessage) = await _votingService.ValidateAndUpdateProgramAsync(id, model);

            if (!success)
            {
                if (errorMessage != null)
                    ModelState.AddModelError(string.Empty, errorMessage);
                return View(CreateProgramView, model);
            }

            if (model.IsPublished)
                TempData["PublishedLink"] = Url.Action("Vote", "Voting", new { id = model.Id }, Request.Scheme);

            TempData["Success"] = "Program updated successfully.";
            return RedirectToAction(nameof(MyPrograms));
        }

        // ── Manage Candidates ──────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ManageCandidates(int programId)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(programId);
            if (program is null) return NotFound();

            if (program.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
                return Forbid();

            ViewBag.Program       = program;
            ViewBag.StartTimeLocal = ToLocal(program.StartTime);
            ViewBag.EndTimeLocal   = ToLocal(program.EndTime);

            return View(ManageCandidatesView, program.Candidates);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> AddCandidate(
            int programId, string name, string candidateCode,
            string? imageUrl, string? description)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(candidateCode))
            {
                TempData["Error"] = "Name and Candidate Code are required.";
                return RedirectToAction(nameof(ManageCandidates), new { programId });
            }

            var program = await _context.VotingPrograms.FindAsync(programId);
            if (program is null) return NotFound();

            if (program.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
                return Forbid();

            bool codeExists = await _context.Candidates
                .AnyAsync(c => c.ProgramId == programId && c.CandidateCode == candidateCode.Trim());

            if (codeExists)
            {
                TempData["Error"] = "Candidate code already exists in this program.";
                return RedirectToAction(nameof(ManageCandidates), new { programId });
            }

            string? safeImageUrl = null;
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                imageUrl = imageUrl.Trim();
                bool validUrl   = Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) &&
                                  (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                bool relativeOk = imageUrl.StartsWith('/');

                if (!validUrl && !relativeOk)
                {
                    TempData["Error"] = "Please enter a valid image URL.";
                    return RedirectToAction(nameof(ManageCandidates), new { programId });
                }

                safeImageUrl = imageUrl;
            }

            var candidate = new Candidate
            {
                Name          = name.Trim(),
                CandidateCode = candidateCode.Trim(),
                Description   = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                ImageUrl      = safeImageUrl,
                ProgramId     = programId,
                CreatedAt     = DateTime.UtcNow,
            };

            try
            {
                _context.Candidates.Add(candidate);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "Candidate could not be saved. Please check the values and try again.";
                return RedirectToAction(nameof(ManageCandidates), new { programId });
            }

            TempData["Success"] = $"Candidate '{name}' added successfully.";
            return RedirectToAction(nameof(ManageCandidates), new { programId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> DeleteCandidate(int id, int programId)
        {
            var candidate = await _context.Candidates.FindAsync(id);
            if (candidate is null) return NotFound();

            var program = await _context.VotingPrograms.FindAsync(programId);
            if (program == null || (program.OwnerId != CurrentUserId && !User.IsInRole("Admin")))
                return Forbid();

            if (candidate.ProgramId != programId) return Forbid();

            try
            {
                _context.Candidates.Remove(candidate);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "Candidate could not be deleted right now.";
                return RedirectToAction(nameof(ManageCandidates), new { programId });
            }

            TempData["Success"] = "Candidate deleted.";
            return RedirectToAction(nameof(ManageCandidates), new { programId });
        }

        
        [HttpGet]
        public async Task<IActionResult> Results(int id)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(id);
            if (program is null) return NotFound();

            if (program.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
                return Forbid();

            var results    = await _votingService.GetResultsAsync(id);
            var totalVotes = results.Sum(r => r.VoteCount);

            ViewBag.Results         = results;
            ViewBag.TotalVotes      = totalVotes;
            ViewBag.BlockchainValid = await _blockchainService.IsChainValidForProgramAsync(_context, id);
            ViewBag.StartTimeLocal  = ToLocal(program.StartTime);
            ViewBag.EndTimeLocal    = ToLocal(program.EndTime);
            ViewBag.NowLocal        = ToLocal(DateTime.UtcNow);

            return View(ResultsView, program);
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

            if (program.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
                return Forbid();

            var voters = await _context.Voters
                .Where(v => v.ProgramId == programId)
                .OrderBy(v => v.Name)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Program = program;
            return View(ManageVotersView, voters);
        }

        [HttpGet]
        public async Task<IActionResult> RegisterVoter(int programId)
        {
            var program = await _context.VotingPrograms.FindAsync(programId);
            if (program == null) return NotFound();

            if (program.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
                return Forbid();

            ViewBag.ProgramId = programId;
            ViewBag.Program   = program;
            return View("~/Views/VotingAdmin/RegisterVoter.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> RegisterVoter(int programId, string name, string email, string? memberId)
        {
            // Ownership check first
            var program = await _context.VotingPrograms.FindAsync(programId);
            if (program == null || (program.OwnerId != CurrentUserId && !User.IsInRole("Admin")))
                return Forbid();

            
            var (success, error) = await _votingService.RegisterVoterByEmailAsync(
                programId, name, email, memberId, registrationSource: "owner");

            if (!success)
            {
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
            // Ownership check
            var program = await _context.VotingPrograms.FindAsync(programId);
            if (program == null || (program.OwnerId != CurrentUserId && !User.IsInRole("Admin")))
                return Forbid();

            // FIX: delegates to service (same as Admin controller)
            var (success, error) = await _votingService.RemoveVoterAsync(voterId, programId);

            TempData[success ? "Success" : "Error"] =
                success ? "Voter removed successfully." : error;

            return RedirectToAction(nameof(ManageVoters), new { programId });
        }

        // ── Shared helpers ─────────────────────────────────────────────────────
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
