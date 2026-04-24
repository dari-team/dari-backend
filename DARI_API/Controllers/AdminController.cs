using DARI_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DARI_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;

        public AdminController(
            IUnitOfWork unitOfWork,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        
        [Authorize(Roles = "Admin")]
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userManager.Users
                .Select(u => new
                {
                    u.Id,
                    u.UserName,
                    u.Email,
                    u.PhoneNumber,
                    u.AccountStatus,
                    u.UserType,
                    u.CreatedAt
                })
                .ToListAsync();

            return Ok(new { message = "Users retrieved successfully.", data = users });
        }

        // =============================================
        // PUT: api/admin/users/{id}/ban
        // Ban a user (sets AccountStatus to Suspended + locks account)
        // =============================================
        [Authorize(Roles = "Admin")]
        [HttpPut("users/{id}/ban")]
        public async Task<IActionResult> BanUser(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (user.AccountStatus == AccountStatus.Suspended)
                return BadRequest(new { message = "User is already banned." });

            user.AccountStatus = AccountStatus.Suspended;

            // Lock the account permanently using Identity lockout
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return StatusCode(500, new { message = "Failed to ban user.", errors = result.Errors });

            return Ok(new { message = $"User '{user.UserName}' has been banned successfully." });
        }

        
        [Authorize(Roles = "Admin")]
        [HttpPut("users/{id}/assign-role")]
        public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Role))
                return BadRequest(new { message = "Role name is required." });

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
                return NotFound(new { message = "User not found." });

            var roleExists = await _roleManager.RoleExistsAsync(dto.Role);
            if (!roleExists)
                return BadRequest(new { message = $"Role '{dto.Role}' does not exist." });

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Contains(dto.Role))
                return BadRequest(new { message = $"User already has the role '{dto.Role}'." });

            var result = await _userManager.AddToRoleAsync(user, dto.Role);
            if (!result.Succeeded)
                return StatusCode(500, new { message = "Failed to assign role.", errors = result.Errors });

            return Ok(new { message = $"Role '{dto.Role}' assigned to user '{user.UserName}' successfully." });
        }

        // =============================================
        // GET: api/admin/listings/pending
        // View all pending listings
        // =============================================
        [Authorize(Roles = "Admin")]
        [HttpGet("listings/pending")]
        public async Task<IActionResult> GetPendingListings()
        {
            var pendingListings = await _unitOfWork.Listings
                .FindAsync(l => l.Status == ListingStatus.Pending);

            var result = pendingListings.Select(l => new
            {
                l.Id,
                l.Title,
                l.Description,
                l.Price,
                l.PropertyType,
                l.ListingType,
                l.Status,
                l.IsApproved,
                l.CreatedAt,
                Lister = l.Lister == null ? null : new
                {
                    l.Lister.Id,
                    l.Lister.UserName,
                    l.Lister.Email
                }
            });

            return Ok(new { message = "Pending listings retrieved successfully.", data = result });
        }

        // =============================================
        // PUT: api/admin/listings/{id}/approve
        // Approve a listing
        // =============================================
        [Authorize(Roles = "Admin")]
        [HttpPut("listings/{id}/approve")]
        public async Task<IActionResult> ApproveListing(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null)
                return NotFound(new { message = "Listing not found." });

            if (listing.IsApproved)
                return BadRequest(new { message = "Listing is already approved." });

            listing.IsApproved = true;
            listing.Status = ListingStatus.Active;
            listing.RejectionReason = null;
            listing.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Listings.Update(listing);
            await _unitOfWork.SaveAsync();

            return Ok(new { message = $"Listing '{listing.Title}' approved successfully." });
        }

        // =============================================
        // PUT: api/admin/listings/{id}/reject
        // Reject a listing with a reason
        // =============================================
        [Authorize(Roles = "Admin")]
        [HttpPut("listings/{id}/reject")]
        public async Task<IActionResult> RejectListing(Guid id, [FromBody] RejectListingDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.RejectionReason))
                return BadRequest(new { message = "Rejection reason is required." });

            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null)
                return NotFound(new { message = "Listing not found." });

            if (listing.Status == ListingStatus.Archived)
                return BadRequest(new { message = "Listing is already rejected/archived." });

            listing.IsApproved = false;
            listing.Status = ListingStatus.Archived;
            listing.RejectionReason = dto.RejectionReason;
            listing.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Listings.Update(listing);
            await _unitOfWork.SaveAsync();

            return Ok(new
            {
                message = $"Listing '{listing.Title}' rejected successfully.",
                reason = dto.RejectionReason
            });
        }
    }

    
    public class AssignRoleDto
    {
        public string Role { get; set; }
    }

    public class RejectListingDto
    {
        public string RejectionReason { get; set; }
    }
}