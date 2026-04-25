using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using AmarTools.Voting.Services.Background;

namespace AmarTools.Voting.Controllers
{
    public class VotingController(
        VotingDbContext context,
        IVotingService votingService,
        IBlockchainService blockchainService,
        IVoteBlockQueue voteBlockQueue) : Controller
    {
        private readonly VotingDbContext _context = context;
        private readonly IVotingService _votingService = votingService;
        private readonly IBlockchainService _blockchainService = blockchainService;
        private readonly IVoteBlockQueue _voteBlockQueue = voteBlockQueue;

        // ── Public voting page ────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Vote(int id)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(id);
            if (program == null) return NotFound();

            var now = DateTime.UtcNow;

            ViewBag.StartTimeLocal = DateTime.SpecifyKind(program.StartTime, DateTimeKind.Utc).ToLocalTime();
            ViewBag.EndTimeLocal = DateTime.SpecifyKind(program.EndTime, DateTimeKind.Utc).ToLocalTime();
            ViewBag.NowLocal = DateTime.SpecifyKind(now, DateTimeKind.Utc).ToLocalTime();

            if (!program.IsPublished || now < program.StartTime || now > program.EndTime)
            {
                ViewBag.Message = "This voting program is not currently active.";
                return View("Closed", program);
            }

            ViewBag.RemainingTime = (program.EndTime > now) ? (TimeSpan?)(program.EndTime - now) : null;
            ViewBag.Candidates = program.Candidates.OrderBy(c => c.Name).ToList();
            ViewBag.Program = program;

            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                ViewBag.IsRegisteredVoter = await _context.Voters
                    .AnyAsync(v => v.ProgramId == id && v.UserId == userId);
            }
            else
            {
                ViewBag.IsRegisteredVoter = false;
            }

            return View(program);
        }

        // ── Public results (read-only, transparency view) ─────────────────────
        [HttpGet]
        public async Task<IActionResult> PublicResults(int id)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(id);
            if (program == null || !program.IsPublished) return NotFound();

            var voteGroups = await _context.Votes
                .Where(v => v.ProgramId == id)
                .GroupBy(v => v.CandidateId)
                .Select(g => new { CandidateId = g.Key, VoteCount = g.Count() })
                .ToListAsync();

            var results = program.Candidates
                .Select(c => new AmarTools.Voting.Models.CandidateResultViewModel
                {
                    Candidate = c,
                    VoteCount = voteGroups.FirstOrDefault(g => g.CandidateId == c.Id)?.VoteCount ?? 0
                })
                .OrderByDescending(r => r.VoteCount)
                .ToList();

            ViewBag.Results = results;
            ViewBag.TotalVotes = voteGroups.Sum(g => g.VoteCount);
            ViewBag.StartTimeLocal = DateTime.SpecifyKind(program.StartTime, DateTimeKind.Utc).ToLocalTime();
            ViewBag.EndTimeLocal = DateTime.SpecifyKind(program.EndTime, DateTimeKind.Utc).ToLocalTime();
            ViewBag.NowLocal = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc).ToLocalTime();
            ViewBag.BlockchainValid = await _blockchainService.IsChainValidForProgramAsync(_context, id);

            return View("PublicResults", program);
        }

        // ── Search endpoint (JSON) ────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Json(Array.Empty<object>());

            q = q.Trim();

            var now = DateTime.UtcNow;

            var programs = await _context.VotingPrograms
                .Where(p => p.IsPublished &&
                            p.ProgramName.ToLower().Contains(q.ToLower()))
                .OrderByDescending(p => p.IsActive)
                .ThenByDescending(p => p.StartTime)
                .Take(8)
                .Select(p => new
                {
                    p.Id,
                    name = p.ProgramName,
                    status = p.IsPublished && now >= p.StartTime && now <= p.EndTime ? "Active" :
                                   now > p.EndTime ? "Ended" : "Upcoming",
                    candidateCount = p.Candidates.Count()
                })
                .ToListAsync();

            return Json(programs);
        }

        // ── Self-join ─────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Join(int programId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var program = await _votingService.GetProgramWithCandidatesAsync(programId);
            if (program == null) return NotFound();

            var now = DateTime.UtcNow;
            if (!program.IsPublished || now > program.EndTime)
            {
                TempData["Error"] = "Registration is closed for this voting program.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }

            bool exists = await _context.Voters
                .AnyAsync(v => v.ProgramId == programId && v.UserId == userId);

            if (exists)
            {
                TempData["Error"] = "You are already registered for this program.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }

            var user = await _context.Users.FindAsync(userId);

            var voter = new Voter
            {
                Name = user?.FullName?.Trim() ?? user?.UserName ?? "Anonymous Voter",
                Email = user?.Email,
                ProgramId = programId,
                UserId = userId,
                RegisteredAt = DateTime.UtcNow,
                RegistrationSource = "self"
            };

            try
            {
                _context.Voters.Add(voter);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "Registration could not be completed right now. Please try again.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }

            TempData["Success"] = "You have been registered for this program. You can now cast your vote.";
            return RedirectToAction(nameof(Vote), new { id = programId });
        }

        // ── Cast Vote ─────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> CastVote(int programId, int candidateId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var program = await _votingService.GetProgramWithCandidatesAsync(programId);
            if (program == null) return NotFound();

            var now = DateTime.UtcNow;
            if (!program.IsPublished || now < program.StartTime || now > program.EndTime)
            {
                TempData["Error"] = "This voting program is no longer active.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }

            var voter = await _context.Voters
                .FirstOrDefaultAsync(v => v.ProgramId == programId && v.UserId == userId);

            if (voter == null)
            {
                TempData["Error"] = "You are not registered to vote in this program. Please contact the program owner.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }

            if (voter.HasVoted)
            {
                TempData["Error"] = "You have already cast your vote in this program.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }

            var candidate = program.Candidates.FirstOrDefault(c => c.Id == candidateId);
            if (candidate == null)
            {
                TempData["Error"] = "Invalid candidate selected.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }

            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                var vote = new Vote
                {
                    ProgramId = programId,
                    CandidateId = candidateId,
                    VoterId = voter.Id,
                    VotedAt = DateTime.UtcNow,
                    VoteSource = "web",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers.UserAgent.ToString()
                };

                _context.Votes.Add(vote);

                voter.HasVoted = true;
                voter.VotedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                try { await _voteBlockQueue.EnqueueAsync(vote.Id); }
                catch (Exception)
                {
                    TempData["Warning"] = "Vote saved, but blockchain block creation encountered an issue. Admin has been notified.";
                }

                TempData["Success"] = "Your vote has been recorded successfully!";
                TempData["ProgramName"] = program.ProgramName;
                TempData["ProgramId"] = programId;

                return RedirectToAction(nameof(ThankYou));
            }
            catch (DbUpdateException ex)
            {
                var inner = ex.InnerException;
                bool uniqueViolation = false;
                try
                {
                    if (inner is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
                        uniqueViolation = true;
                }
                catch { }

                if (uniqueViolation || inner?.Message?.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true)
                {
                    TempData["Error"] = "You have already cast your vote in this program.";
                    return RedirectToAction(nameof(Vote), new { id = programId });
                }

                TempData["Error"] = "An error occurred while recording your vote. Please try again.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }
            catch (Exception)
            {
                TempData["Error"] = "An error occurred while recording your vote. Please try again.";
                return RedirectToAction(nameof(Vote), new { id = programId });
            }
        }

        [HttpGet]
        public IActionResult ThankYou() => View();

        [HttpGet]
        public IActionResult Closed() => View();
    }
}