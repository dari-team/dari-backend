using DARI_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DARI_API.Controllers
{
    // Simulated payment for graduation project — no real charge.
    // On "success" we mark the user verified and extend their subscription.
    [ApiController]
    [Authorize]
    [Route("api/payment")]
    public class PaymentController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public PaymentController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        private Guid? CurrentUserId()
        {
            var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(s, out var id) ? id : (Guid?)null;
        }

        // POST /api/payment/confirm
        // Plan id format: agent_monthly | agent_quarterly | agent_annual | lister_plus | lister_pro
        [HttpPost("confirm")]
        public async Task<IActionResult> ConfirmPayment([FromBody] ConfirmPaymentDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.PlanId))
                return BadRequest(new { message = "PlanId is required." });

            var uid = CurrentUserId();
            if (uid == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(uid.Value.ToString());
            if (user == null) return NotFound(new { message = "User not found." });

            if (user.UserType != UserType.Lister)
                return BadRequest(new { message = "Only listers/agents can subscribe." });

            // Determine plan duration + max listings (graduation-project simulated)
            int days, maxListings;
            switch (dto.PlanId.ToLowerInvariant())
            {
                case "agent_monthly":   days = 30;  maxListings = 50;  break;
                case "agent_quarterly": days = 90;  maxListings = 50;  break;
                case "agent_annual":    days = 365; maxListings = 100; break;
                case "lister_plus":     days = 30;  maxListings = 5;   break;
                case "lister_pro":      days = 30;  maxListings = 15;  break;
                default: return BadRequest(new { message = $"Unknown plan '{dto.PlanId}'." });
            }

            var now = DateTime.UtcNow;
            var newExpiry = (user.SubscriptionEndDate.HasValue && user.SubscriptionEndDate.Value > now
                ? user.SubscriptionEndDate.Value
                : now).AddDays(days);

            user.SubscriptionEndDate = newExpiry;
            user.MaxListings = Math.Max(user.MaxListings, maxListings);
            user.IsVerified = true; // Lister verified on payment; Agents are always verified after payment.
            user.UpdatedAt = now;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return StatusCode(500, new { message = "Failed to confirm payment.", errors = result.Errors });

            return Ok(new
            {
                message = "Payment confirmed. Account verified.",
                isVerified = true,
                subscriptionEndDate = newExpiry,
                maxListings = user.MaxListings,
                planId = dto.PlanId
            });
        }
    }

    public class ConfirmPaymentDto
    {
        public string PlanId { get; set; } = "";
    }
}
