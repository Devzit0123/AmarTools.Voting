using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using AmarTools.Voting.Services.Background;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AmarTools.Voting.Controllers
{
    public class VotingController(
        VotingDbContext context,
        IVotingService votingService,
        IBlockchainService blockchainService,
        IVoteBlockQueue voteBlockQueue) : Controller
    {
        private readonly VotingDbContext          _context           = context;
        private readonly IVotingService           _votingService     = votingService;
        private readonly IBlockchainService       _blockchainService = blockchainService;
        private readonly IVoteBlockQueue          _voteBlockQueue    = voteBlockQueue;

        // ── Public voting page ────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Vote(int id)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(id);
            if (program == null || !program.IsPublished) return NotFound();

            var now = DateTime.UtcNow;

            ViewBag.StartTimeUtc = DateTime.SpecifyKind(program.StartTime, DateTimeKind.Utc);
            ViewBag.EndTimeUtc   = DateTime.SpecifyKind(program.EndTime,   DateTimeKind.Utc);
            ViewBag.NowUtc       = DateTime.SpecifyKind(now,               DateTimeKind.Utc);

            if (!program.IsOpenAt(now))
            {
                ViewBag.Message = "This voting program is not currently active.";
                return View("Closed", program);
            }

            ViewBag.RemainingTime = program.EndTime > now ? (TimeSpan?)(program.EndTime - now) : null;
            ViewBag.Candidates    = program.Candidates.OrderBy(c => c.Name).ToList();
            ViewBag.Program       = program;

            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var voter = string.IsNullOrEmpty(userId)
                    ? null
                    : await _context.Voters.FirstOrDefaultAsync(v => v.ProgramId == id && v.UserId == userId);
                ViewBag.IsRegisteredVoter = voter is not null;
                ViewBag.HasVoted = voter?.HasVoted ?? false;
            }
            else
            {
                ViewBag.IsRegisteredVoter = false;
                ViewBag.HasVoted = false;
            }

            return View(program);
        }

        // ── Public results (read-only, transparency view) ─────────────────────
        [HttpGet]
        public async Task<IActionResult> PublicResults(int id)
        {
            var program = await _votingService.GetProgramWithCandidatesAsync(id);
            if (program == null || !program.IsPublished) return NotFound();

            var results    = await _votingService.GetResultsAsync(id);
            var totalVotes = results.Sum(r => r.VoteCount);

            ViewBag.Results        = results;
            ViewBag.TotalVotes     = totalVotes;
            ViewBag.StartTimeUtc = DateTime.SpecifyKind(program.StartTime, DateTimeKind.Utc);
            ViewBag.EndTimeUtc   = DateTime.SpecifyKind(program.EndTime,   DateTimeKind.Utc);
            ViewBag.NowUtc       = DateTime.SpecifyKind(DateTime.UtcNow,   DateTimeKind.Utc);
            try
            {
                ViewBag.BlockchainValid = await _blockchainService.IsChainValidForProgramAsync(_context, id);
            }
            catch
            {
                ViewBag.BlockchainValid = false;
            }

            return View("PublicResults", program);
        }

        // ── Search endpoint (JSON) ────────────────────────────────────────────
        // FIX: rate-limited to 30 requests/min per IP
        [HttpGet]
        [EnableRateLimiting("search")]
        public async Task<IActionResult> Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Json(Array.Empty<object>());

            q = q.Trim();
            var now = DateTime.UtcNow;

            var programs = await _context.VotingPrograms
                .Where(p => p.IsPublished &&
                            p.ProgramName.ToLower().Contains(q.ToLower()))
                .OrderByDescending(p => p.StartTime <= now && p.EndTime > now)
                .ThenByDescending(p => p.StartTime)
                .Take(8)
                .Select(p => new
                {
                    p.Id,
                    name   = p.ProgramName,
                    status = p.IsPublished && now >= p.StartTime && now < p.EndTime ? "Active"
                           : now >= p.EndTime ? "Ended" : "Upcoming",
                    candidateCount = p.Candidates.Count()
                })
                .ToListAsync();

            return Json(programs);
        }

        // ── Self-join ─────────────────────────────────────────────────────────
        // FIX: rate-limited by authenticated voter, or by IP before authentication
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        [EnableRateLimiting("voting")]
        public async Task<IActionResult> Join(int programId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var program = await _votingService.GetProgramWithCandidatesAsync(programId);
            if (program == null) return NotFound();

            var now = DateTime.UtcNow;
            if (!program.IsPublished || now >= program.EndTime)
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

            TempData["Error"] = "Only the program owner can register voters for this program.";
            return RedirectToAction(nameof(Vote), new { id = programId });
        }

        // ── Cast Vote ─────────────────────────────────────────────────────────
        // FIX: rate-limited by authenticated voter, or by IP before authentication
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        [EnableRateLimiting("voting")]
        public async Task<IActionResult> CastVote(int programId, int candidateId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var program = await _votingService.GetProgramWithCandidatesAsync(programId);
            if (program == null) return NotFound();

            var now = DateTime.UtcNow;
            if (!program.IsOpenAt(now))
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
                await using var transaction = await _context.Database
                    .BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                var vote = new Vote
                {
                    ProgramId   = programId,
                    CandidateId = candidateId,
                    VoterId     = voter.Id,
                    VotedAt     = DateTime.UtcNow,
                    VoteSource  = "web",
                    IpAddress   = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent   = Request.Headers.UserAgent.ToString()[..Math.Min(Request.Headers.UserAgent.ToString().Length, 512)],
                };

                _context.Votes.Add(vote);
                voter.HasVoted = true;
                voter.VotedAt  = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                
                bool queued = _voteBlockQueue.TryEnqueue(vote.Id);
                if (!queued)
                {
                    TempData["Warning"] =
                        "Your vote was recorded, but the blockchain block could not be " +
                        "queued right now due to high load. The integrity record will " +
                        "be created shortly.";
                }

                TempData["Success"]     = "Your vote has been recorded successfully!";
                TempData["VoteRecorded"] = true;
                TempData["ProgramName"] = program.ProgramName;
                TempData["ProgramId"]   = programId;

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

                if (uniqueViolation ||
                    inner?.Message?.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true)
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
        public IActionResult ThankYou()
        {
            if (TempData["VoteRecorded"] is not true)
                return RedirectToAction(nameof(Vote));

            return View();
        }
        [HttpGet] public IActionResult Closed()   => View();
    }
}
