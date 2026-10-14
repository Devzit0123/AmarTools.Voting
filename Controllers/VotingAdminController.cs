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

        // ── Dashboard ──────────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var programs = await _votingService.GetAllProgramsForDashboardAsync();

            return View(programs);
        }

        // ── Create Program ─────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Create()
        {
            var utcNow = DateTime.UtcNow;
            var model    = new VotingProgram
            {
                StartTime = utcNow,
                EndTime   = utcNow.AddHours(1),
                ValuesAreUtc = true
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
            {
                model.ValuesAreUtc = false;
                return View("CreateProgram", model);
            }

            var (success, errorMessage) = await _votingService.ValidateAndCreateProgramAsync(model);

            if (!success)
            {
                if (errorMessage != null)
                    ModelState.AddModelError(string.Empty, errorMessage);
                return View("CreateProgram", model);
            }

            if (model.IsPublished)
                TempData["PublishedLink"] = PublicLink(model);

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

            program.StartTime = DateTime.SpecifyKind(program.StartTime, DateTimeKind.Utc);
            program.EndTime   = DateTime.SpecifyKind(program.EndTime, DateTimeKind.Utc);
            program.ValuesAreUtc = true;

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
                TempData["PublishedLink"] = PublicLink(model);

            TempData["Success"] = "Program updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        
        [HttpGet]
        public async Task<IActionResult> ManageCandidates(int programId)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(programId);
            if (program is null) return NotFound();

            program.LoadedVoteCount = await _context.Votes.CountAsync(v => v.ProgramId == programId);
            ViewBag.Program       = program;

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

            return View(program);
        }

        [HttpGet]
        public async Task<IActionResult> ExportResults(int id)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(id);
            if (program is null) return NotFound();

            var results = await _votingService.GetResultsAsync(id);
            return File(ResultsCsv.Build(program, results), "text/csv", ResultsCsv.FileName(program));
        }

        // ── Users & roles ──────────────────────────────────────────────────────
        // New accounts are voters. Only an admin can let someone create programs.
        public const string ProgramOwnerRole = "ProgramOwner";

        public sealed record UserRow(string Id, string FullName, string Email, bool EmailConfirmed, bool IsProgramOwner, bool IsAdmin);

        [HttpGet]
        public async Task<IActionResult> Users(string? q)
        {
            var roleIds = await _context.Roles
                .Where(r => r.Name == ProgramOwnerRole || r.Name == "Admin")
                .ToDictionaryAsync(r => r.Name!, r => r.Id);
            var ownerRoleId = roleIds.GetValueOrDefault(ProgramOwnerRole);
            var adminRoleId = roleIds.GetValueOrDefault("Admin");

            var users = _context.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                users = users.Where(u => (u.Email != null && u.Email.ToLower().Contains(term)) ||
                                         u.FullName.ToLower().Contains(term));
            }

            var rows = await users
                .OrderBy(u => u.Email)
                .Take(200)
                .Select(u => new UserRow(
                    u.Id,
                    u.FullName,
                    u.Email ?? "",
                    u.EmailConfirmed,
                    _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == ownerRoleId),
                    _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId)))
                .ToListAsync();

            ViewBag.Query = q;
            return View(rows);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("admin")]
        public async Task<IActionResult> SetProgramOwner(string userId, bool grant, string? q)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return NotFound();

            if (grant && !user.EmailConfirmed)
            {
                TempData["Error"] = "Only accounts with a confirmed email can become program owners.";
                return RedirectToAction(nameof(Users), new { q });
            }

            var isOwner = await _userManager.IsInRoleAsync(user, ProgramOwnerRole);
            IdentityResult result = IdentityResult.Success;
            if (grant && !isOwner)
                result = await _userManager.AddToRoleAsync(user, ProgramOwnerRole);
            else if (!grant && isOwner)
                result = await _userManager.RemoveFromRoleAsync(user, ProgramOwnerRole);

            if (result.Succeeded)
                TempData["Success"] = grant
                    ? $"{user.Email} can now create voting programs."
                    : $"{user.Email} is no longer a program owner. Their existing programs are kept.";
            else
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));

            return RedirectToAction(nameof(Users), new { q });
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

            // Same rule as ProgramOwnerController.Delete (D05): the cascade FKs would
            // otherwise remove the votes and blockchain blocks with the program.
            if (await _context.Votes.AnyAsync(v => v.ProgramId == id))
            {
                TempData["Error"] = "A program with recorded votes cannot be deleted.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.VotingPrograms.Remove(program);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "This program could not be deleted right now.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Program deleted.";
            return RedirectToAction(nameof(Index));
        }

        
        // Shareable link: /v/{slug} when the program has a custom slug, otherwise /Voting/Vote/{id}.
        private string? PublicLink(VotingProgram program) =>
            string.IsNullOrEmpty(program.Slug)
                ? Url.Action("Vote", "Voting", new { id = program.Id }, Request.Scheme)
                : Url.Action("BySlug", "Voting", new { slug = program.Slug }, Request.Scheme);

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
