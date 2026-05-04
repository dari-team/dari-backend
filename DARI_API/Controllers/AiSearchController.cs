using System.Security.Claims;
using DARI_API.AiSearch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DARI_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AiSearchController : ControllerBase
{
    private readonly AiSearchPipeline _pipeline;
    private readonly IAiExtractionService _ai;
    private readonly UserAiSearchQuota _quota;

    // Word-count bounds applied to every authenticated AI search query.
    // - Lower bound (7) prevents single-keyword queries that AI can't usefully
    //   structure ("villa", "Maadi") and burn quota.
    // - Upper bound (20) caps prompt-injection / token-burn surface and keeps
    //   completion latency stable.
    private const int MinWords = 7;
    private const int MaxWords = 20;

    // Per-user weekly limit. 3 calls per rolling 7-day window — counted on
    // the server, NOT in localStorage, so it can't be reset by clearing
    // browser data. See UserAiSearchQuota for the in-memory implementation.
    private const int WeeklyQuota = 3;

    public AiSearchController(AiSearchPipeline pipeline, IAiExtractionService ai, UserAiSearchQuota quota)
    {
        _pipeline = pipeline;
        _ai = ai;
        _quota = quota;
    }

    public class SearchRequest
    {
        public string Query { get; set; } = "";
    }

    // Full Layer 2-7 pipeline: AI extract → plausibility → SQL FTS → rank → log.
    // Authenticated route — no anonymous AI search. Three layers of abuse
    // protection compose here:
    //   1. [Authorize]              — must be logged in (JWT)
    //   2. UserAiSearchQuota        — per-account 3/week (account-bound, not device-bound)
    //   3. [EnableRateLimiting]     — per-IP token-bucket (catches scripted abuse)
    [Authorize]
    [HttpPost("search")]
    [EnableRateLimiting("ai-search")]
    public async Task<IActionResult> Search([FromBody] SearchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Query))
            return BadRequest(new { error = "query is required" });

        var query = req.Query.Trim();
        var words = query.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < MinWords)
            return BadRequest(new { error = $"Query too short — please use at least {MinWords} words.", code = "QUERY_TOO_SHORT" });
        if (words.Length > MaxWords)
            return BadRequest(new { error = $"Query too long — please keep it under {MaxWords} words.", code = "QUERY_TOO_LONG" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid user token." });

        // Admins bypass the per-user quota — they need unconstrained access
        // for moderation, testing, and demos. They still go through Layer 7
        // logging so usage is observable; only the cap is removed.
        var isAdmin = User.IsInRole("Admin");

        // Quota check — read-only first; we record usage AFTER a successful
        // pipeline run so failed calls don't count against the user.
        if (!isAdmin)
        {
            var (allowed, used, resetAt) = _quota.PeekStatus(userId, WeeklyQuota);
            if (!allowed)
                return StatusCode(429, new
                {
                    error      = $"You've reached your weekly AI search limit ({WeeklyQuota}/week).",
                    code       = "WEEKLY_QUOTA_EXCEEDED",
                    used,
                    limit      = WeeklyQuota,
                    resetAtUtc = resetAt,
                });
        }

        try
        {
            var result = await _pipeline.SearchAsync(query, ct);
            if (!isAdmin) _quota.Record(userId);
            return Ok(result);
        }
        catch (AiProviderQuotaException ex)
        {
            // The AI provider hit ITS daily/per-minute quota — not the user's.
            // Don't consume the user's slot. The frontend distinguishes this
            // from per-user quota by the AI_SERVICE_QUOTA code.
            return StatusCode(503, new
            {
                error = "AI service is at capacity. Please try again later.",
                code  = "AI_SERVICE_QUOTA",
                detail = ex.Message,
            });
        }
        catch (AiExtractionException ex)
        {
            return StatusCode(502, new { error = ex.Message, code = "AI_EXTRACTION_FAILED" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Returns the user's current quota status without consuming it. Lets the
    // frontend disable the search button before the user types anything.
    [Authorize]
    [HttpGet("quota")]
    public IActionResult Quota()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid user token." });

        // Mirror the /search bypass — admins see "unlimited" so the chip in
        // the panel doesn't tick down (because their searches don't tick).
        if (User.IsInRole("Admin"))
        {
            return Ok(new
            {
                limit      = (int?)null,
                used       = 0,
                remaining  = (int?)null,
                allowed    = true,
                resetAtUtc = DateTime.UtcNow.AddYears(1),  // far-future sentinel
                unlimited  = true,
            });
        }

        var (allowed, used, resetAt) = _quota.PeekStatus(userId, WeeklyQuota);
        return Ok(new
        {
            limit      = WeeklyQuota,
            used,
            remaining  = WeeklyQuota - used,
            allowed,
            resetAtUtc = resetAt,
            unlimited  = false,
        });
    }

    // Diagnostic endpoint — Layer 2 only, no DB. Useful for debugging the
    // prompt/extraction in isolation. Not rate-limited; remove or gate to
    // admin role for production.
    [HttpPost("extract")]
    public async Task<IActionResult> Extract([FromBody] SearchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Query))
            return BadRequest(new { error = "query is required" });

        try
        {
            var result = await _ai.ExtractAsync(req.Query, ct);
            return Ok(new
            {
                parsed     = result.Parsed,
                language   = result.Language,
                latencyMs  = result.LatencyMs,
                retryUsed  = result.RetryUsed,
                model      = result.ModelUsed,
                tokens     = new { prompt = result.PromptTokens, completion = result.CompletionTokens },
            });
        }
        catch (AiExtractionException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
