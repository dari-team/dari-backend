using DARI_API.Models;
using DARI_API.Services;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DARI_API.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;
        private readonly ICloudinaryService _cloudinary;

        public AdminController(
            IUnitOfWork unitOfWork,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            ICloudinaryService cloudinary)
        {
            _unitOfWork = unitOfWork;
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _cloudinary = cloudinary;
        }

        // ============================================================
        // Users
        // ============================================================

        private async Task<object> ProjectUser(ApplicationUser u)
        {
            var roles = await _userManager.GetRolesAsync(u);
            return new
            {
                u.Id,
                u.UserName,
                u.Name,
                u.Email,
                u.PhoneNumber,
                u.AccountStatus,
                u.UserType,
                u.CustomerType,
                u.ListerType,
                u.AgencyName,
                u.LicenseNumber,
                u.IsVerified,
                u.MaxListings,
                u.SubscriptionEndDate,
                u.SuspendedUntil,
                u.SuspensionReason,
                u.BanReason,
                u.CreatedAt,
                u.UpdatedAt,
                Roles = roles
            };
        }

        // GET /api/admin/users
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers([FromQuery] string? search = null, [FromQuery] string? status = null, [FromQuery] string? role = null)
        {
            var q = _userManager.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(u =>
                    (u.Name != null && u.Name.ToLower().Contains(s)) ||
                    (u.Email != null && u.Email.ToLower().Contains(s)) ||
                    (u.UserName != null && u.UserName.ToLower().Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AccountStatus>(status, true, out var st))
                q = q.Where(u => u.AccountStatus == st);

            var users = await q.OrderByDescending(u => u.CreatedAt).ToListAsync();

            var result = new List<object>();
            foreach (var u in users)
            {
                if (!string.IsNullOrWhiteSpace(role))
                {
                    var roles = await _userManager.GetRolesAsync(u);
                    if (!roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase))) continue;
                }
                result.Add(await ProjectUser(u));
            }

            return Ok(new { message = "Users retrieved successfully.", data = result });
        }

        // GET /api/admin/users/{id}
        [HttpGet("users/{id}")]
        public async Task<IActionResult> GetUserById(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound(new { message = "User not found." });

            var listingCount  = await _db.Listings.CountAsync(l => l.ListerId == id);
            var inquiryCount  = await _db.Inquiries.CountAsync(i => i.CustomerId == id || i.ListerId == id);
            var totalViews    = await _db.Listings.Where(l => l.ListerId == id).SumAsync(l => (int?)l.ViewCount) ?? 0;

            var profile = await ProjectUser(user);

            return Ok(new
            {
                message = "User profile retrieved.",
                data = new
                {
                    profile,
                    stats = new { listingCount, inquiryCount, totalViews }
                }
            });
        }

        // PUT /api/admin/users/{id}/ban — permanent
        [HttpPut("users/{id}/ban")]
        public async Task<IActionResult> BanUser(Guid id, [FromBody] BanUserDto? dto = null)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound(new { message = "User not found." });

            if (user.AccountStatus == AccountStatus.Banned)
                return BadRequest(new { message = "User is already banned." });

            user.AccountStatus = AccountStatus.Banned;
            user.BanReason = dto?.Reason;
            user.SuspendedUntil = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return StatusCode(500, new { message = "Failed to ban user.", errors = result.Errors });

            return Ok(new { message = $"User '{user.UserName}' has been banned permanently." });
        }

        // PATCH /api/admin/users/{id}/suspend — time-bounded
        [HttpPatch("users/{id}/suspend")]
        public async Task<IActionResult> SuspendUser(Guid id, [FromBody] SuspendUserDto dto)
        {
            if (dto == null) return BadRequest(new { message = "Body is required." });
            if (dto.Days <= 0) return BadRequest(new { message = "Days must be > 0." });

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound(new { message = "User not found." });

            if (user.AccountStatus == AccountStatus.Banned)
                return BadRequest(new { message = "Cannot suspend a banned user. Reactivate first." });

            var until = DateTime.UtcNow.AddDays(dto.Days);
            user.AccountStatus = AccountStatus.Suspended;
            user.SuspendedUntil = until;
            user.SuspensionReason = dto.Reason;
            user.UpdatedAt = DateTime.UtcNow;

            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, until);

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return StatusCode(500, new { message = "Failed to suspend user.", errors = result.Errors });

            return Ok(new
            {
                message = $"User '{user.UserName}' suspended for {dto.Days} day(s).",
                suspendedUntil = until
            });
        }

        // PATCH /api/admin/users/{id}/reactivate
        [HttpPatch("users/{id}/reactivate")]
        public async Task<IActionResult> ReactivateUser(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound(new { message = "User not found." });

            if (user.AccountStatus == AccountStatus.Active)
                return BadRequest(new { message = "User is already active." });

            user.AccountStatus = AccountStatus.Active;
            user.SuspendedUntil = null;
            user.SuspensionReason = null;
            user.BanReason = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userManager.SetLockoutEndDateAsync(user, null);

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return StatusCode(500, new { message = "Failed to reactivate user.", errors = result.Errors });

            return Ok(new { message = $"User '{user.UserName}' has been reactivated." });
        }

        // PATCH /api/admin/users/{id}/verify — toggles or sets explicitly
        [HttpPatch("users/{id}/verify")]
        public async Task<IActionResult> VerifyUser(Guid id, [FromBody] VerifyUserDto? dto = null)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound(new { message = "User not found." });

            if (user.UserType != UserType.Lister)
                return BadRequest(new { message = "Only listers/agents can be verified." });

            user.IsVerified = dto?.IsVerified ?? !user.IsVerified;
            user.UpdatedAt = DateTime.UtcNow;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return StatusCode(500, new { message = "Failed to update verification.", errors = result.Errors });

            return Ok(new { message = user.IsVerified ? "User verified." : "User unverified.", isVerified = user.IsVerified });
        }

        // PUT /api/admin/users/{id}/role
        [HttpPut("users/{id}/role")]
        public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Role))
                return BadRequest(new { message = "Role name is required." });

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound(new { message = "User not found." });

            var roleExists = await _roleManager.RoleExistsAsync(dto.Role);
            if (!roleExists)
                return BadRequest(new { message = $"Role '{dto.Role}' does not exist." });

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                var rem = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!rem.Succeeded)
                    return StatusCode(500, new { message = "Failed to remove existing roles.", errors = rem.Errors });
            }

            var add = await _userManager.AddToRoleAsync(user, dto.Role);
            if (!add.Succeeded)
                return StatusCode(500, new { message = "Failed to assign role.", errors = add.Errors });

            // Keep UserType in sync with the role (best-effort)
            if (string.Equals(dto.Role, "Customer", StringComparison.OrdinalIgnoreCase))
                user.UserType = UserType.Customer;
            else if (string.Equals(dto.Role, "Lister", StringComparison.OrdinalIgnoreCase))
                user.UserType = UserType.Lister;
            else if (string.Equals(dto.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                user.UserType = UserType.Admin;

            user.UpdatedAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            return Ok(new { message = $"Role '{dto.Role}' assigned to user '{user.UserName}'." });
        }

        // DELETE /api/admin/users/{id}
        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound(new { message = "User not found." });

            // Cascade: remove user's listings (and child views/inquiries via Restrict guards)
            var listings = await _db.Listings.Where(l => l.ListerId == id).ToListAsync();
            foreach (var l in listings)
            {
                var addr = await _db.Addresses.FirstOrDefaultAsync(a => a.ListingId == l.Id);
                if (addr != null) _db.Addresses.Remove(addr);

                var views = _db.ListingViews.Where(v => v.ListingId == l.Id);
                _db.ListingViews.RemoveRange(views);

                var images = _db.Images.Where(i => i.ListingId == l.Id);
                _db.Images.RemoveRange(images);

                _db.Listings.Remove(l);
            }

            // Remove inquiries + messages where user is on either side
            var inquiries = await _db.Inquiries
                .Where(i => i.CustomerId == id || i.ListerId == id).ToListAsync();
            foreach (var i in inquiries)
            {
                var msgs = _db.Messages.Where(m => m.InquiryId == i.Id);
                _db.Messages.RemoveRange(msgs);
                _db.Inquiries.Remove(i);
            }

            // Remove notifications + listing views authored by user
            _db.Notifications.RemoveRange(_db.Notifications.Where(n => n.UserId == id));
            _db.ListingViews.RemoveRange(_db.ListingViews.Where(v => v.UserId == id));

            await _db.SaveChangesAsync();

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return StatusCode(500, new { message = "Failed to delete user.", errors = result.Errors });

            return Ok(new { message = $"User '{user.UserName}' deleted successfully." });
        }

        // ============================================================
        // Listings
        // ============================================================

        private static object ProjectListing(Listing l) => new
        {
            l.Id,
            l.Title,
            l.Description,
            l.Price,
            l.Bedrooms,
            l.Bathrooms,
            l.AreaSize,
            l.PropertyType,
            l.ListingType,
            l.ListingKind,
            l.Status,
            l.IsApproved,
            l.IsFeatured,
            l.ViewCount,
            l.CoverImageUrl,
            l.RejectionReason,
            l.AiQualityScore,
            l.LifestyleScore,
            l.CreatedAt,
            l.UpdatedAt,
            Address = l.Address == null ? null : new
            {
                l.Address.Street,
                l.Address.City,
                l.Address.Region,
                l.Address.Country
            },
            Lister = l.Lister == null ? null : new
            {
                l.Lister.Id,
                l.Lister.Name,
                l.Lister.UserName,
                l.Lister.Email,
                l.Lister.IsVerified
            },
            Images = (l.Images ?? new List<Image>()).Select(i => new
            {
                i.Id, i.Url, i.SortOrder
            })
        };

        // GET /api/admin/listings  (also supports ?status=pending|active|archived|...)
        [HttpGet("listings")]
        public async Task<IActionResult> GetAllListings([FromQuery] string? status = null)
        {
            var q = _db.Listings
                .Include(l => l.Address)
                .Include(l => l.Lister)
                .Include(l => l.Images)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                    q = q.Where(l => !l.IsApproved && l.Status != ListingStatus.Archived);
                else if (Enum.TryParse<ListingStatus>(status, true, out var st))
                    q = q.Where(l => l.Status == st);
            }

            var listings = await q.OrderByDescending(l => l.CreatedAt).ToListAsync();
            return Ok(new { message = "Listings retrieved.", data = listings.Select(ProjectListing) });
        }

        // GET /api/admin/listings/{id}
        [HttpGet("listings/{id}")]
        public async Task<IActionResult> GetListingById(Guid id)
        {
            var listing = await _db.Listings
                .Include(l => l.Address)
                .Include(l => l.Lister)
                .Include(l => l.Images)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (listing == null) return NotFound(new { message = "Listing not found." });
            return Ok(new { message = "Listing retrieved.", data = ProjectListing(listing) });
        }

        // PUT /api/admin/listings/{id}/approve
        [HttpPut("listings/{id}/approve")]
        public async Task<IActionResult> ApproveListing(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null) return NotFound(new { message = "Listing not found." });

            if (listing.IsApproved && listing.Status == ListingStatus.Active)
                return BadRequest(new { message = "Listing is already approved." });

            listing.IsApproved = true;
            listing.Status = ListingStatus.Active;
            listing.RejectionReason = null;
            listing.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Listings.Update(listing);

            await _unitOfWork.Notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = listing.ListerId,
                Title = "Listing approved",
                Body = $"Your listing \"{listing.Title}\" is now live.",
                Type = NotificationType.ListingApproved,
                Seen = false,
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveAsync();
            return Ok(new { message = $"Listing '{listing.Title}' approved." });
        }

        // PUT /api/admin/listings/{id}/reject
        [HttpPut("listings/{id}/reject")]
        public async Task<IActionResult> RejectListing(Guid id, [FromBody] RejectListingDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.RejectionReason))
                return BadRequest(new { message = "Rejection reason is required." });

            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null) return NotFound(new { message = "Listing not found." });

            listing.IsApproved = false;
            listing.Status = ListingStatus.Archived;
            listing.RejectionReason = dto.RejectionReason.Length > 255
                ? dto.RejectionReason.Substring(0, 255)
                : dto.RejectionReason;
            listing.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Listings.Update(listing);

            await _unitOfWork.Notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = listing.ListerId,
                Title = "Listing rejected",
                Body = $"Your listing \"{listing.Title}\" was rejected: {listing.RejectionReason}",
                Type = NotificationType.ListingRejected,
                Seen = false,
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveAsync();
            return Ok(new { message = $"Listing '{listing.Title}' rejected.", reason = listing.RejectionReason });
        }

        // PATCH /api/admin/listings/{id}/unpublish
        [HttpPatch("listings/{id}/unpublish")]
        public async Task<IActionResult> UnpublishListing(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null) return NotFound(new { message = "Listing not found." });

            if (listing.Status != ListingStatus.Active)
                return BadRequest(new { message = "Only active listings can be unpublished." });

            listing.Status = ListingStatus.Archived;
            listing.IsApproved = false;
            listing.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Listings.Update(listing);

            await _unitOfWork.Notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = listing.ListerId,
                Title = "Listing unpublished",
                Body = $"Your listing \"{listing.Title}\" was taken down by an administrator.",
                Type = NotificationType.ListingRejected,
                Seen = false,
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveAsync();
            return Ok(new { message = $"Listing '{listing.Title}' unpublished." });
        }

        // PATCH /api/admin/listings/{id}/re-approve
        [HttpPatch("listings/{id}/re-approve")]
        public async Task<IActionResult> ReApproveListing(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null) return NotFound(new { message = "Listing not found." });

            listing.Status = ListingStatus.Active;
            listing.IsApproved = true;
            listing.RejectionReason = null;
            listing.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Listings.Update(listing);

            await _unitOfWork.Notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = listing.ListerId,
                Title = "Listing re-approved",
                Body = $"Your listing \"{listing.Title}\" is live again.",
                Type = NotificationType.ListingApproved,
                Seen = false,
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveAsync();
            return Ok(new { message = $"Listing '{listing.Title}' re-approved." });
        }

        // DELETE /api/admin/listings/{id}
        [HttpDelete("listings/{id}")]
        public async Task<IActionResult> DeleteListing(Guid id)
        {
            var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == id);
            if (listing == null) return NotFound(new { message = "Listing not found." });

            var addr = await _db.Addresses.FirstOrDefaultAsync(a => a.ListingId == id);
            if (addr != null) _db.Addresses.Remove(addr);

            _db.ListingViews.RemoveRange(_db.ListingViews.Where(v => v.ListingId == id));

            // Delete the Cloudinary assets before dropping the image rows, so
            // storage doesn't fill up with images of deleted listings.
            var images = await _db.Images.Where(i => i.ListingId == id).ToListAsync();
            foreach (var img in images)
            {
                if (string.IsNullOrWhiteSpace(img.PublicId)) continue;
                try { await _cloudinary.DeleteAsync(img.PublicId); }
                catch { /* best-effort — don't block the delete */ }
            }
            _db.Images.RemoveRange(images);

            // Inquiries on this listing
            var inquiries = await _db.Inquiries.Where(i => i.ListingId == id).ToListAsync();
            foreach (var i in inquiries)
            {
                _db.Messages.RemoveRange(_db.Messages.Where(m => m.InquiryId == i.Id));
                _db.Inquiries.Remove(i);
            }

            _db.Listings.Remove(listing);
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Listing '{listing.Title}' deleted." });
        }

        // ============================================================
        // Inquiries + Messages
        // ============================================================

        // GET /api/admin/inquiries
        [HttpGet("inquiries")]
        public async Task<IActionResult> GetAllInquiries([FromQuery] string? status = null)
        {
            var q = _db.Inquiries.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InquiryStatus>(status, true, out var st))
                q = q.Where(i => i.Status == st);

            var inquiries = await q.OrderByDescending(i => i.CreatedAt).ToListAsync();

            var result = new List<object>();
            foreach (var inq in inquiries)
            {
                var listing  = await _db.Listings.FirstOrDefaultAsync(l => l.Id == inq.ListingId);
                var customer = await _userManager.FindByIdAsync(inq.CustomerId.ToString());
                var lister   = await _userManager.FindByIdAsync(inq.ListerId.ToString());
                var msgCount = await _db.Messages.CountAsync(m => m.InquiryId == inq.Id);

                result.Add(new
                {
                    inq.Id,
                    inq.Status,
                    inq.CreatedAt,
                    inq.ListingId,
                    ListingTitle = listing?.Title,
                    Customer = customer == null ? null : new { customer.Id, customer.Name, customer.Email },
                    Lister   = lister   == null ? null : new { lister.Id,   lister.Name,   lister.Email   },
                    MessageCount = msgCount
                });
            }

            return Ok(new { message = "Inquiries retrieved.", data = result });
        }

        // GET /api/admin/inquiries/{id}
        [HttpGet("inquiries/{id}")]
        public async Task<IActionResult> GetInquiryById(Guid id)
        {
            var inq = await _db.Inquiries.FirstOrDefaultAsync(i => i.Id == id);
            if (inq == null) return NotFound(new { message = "Inquiry not found." });

            var listing  = await _db.Listings.FirstOrDefaultAsync(l => l.Id == inq.ListingId);
            var customer = await _userManager.FindByIdAsync(inq.CustomerId.ToString());
            var lister   = await _userManager.FindByIdAsync(inq.ListerId.ToString());
            var messages = await _db.Messages
                .Where(m => m.InquiryId == id)
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            return Ok(new
            {
                message = "Inquiry retrieved.",
                data = new
                {
                    inq.Id,
                    inq.Status,
                    inq.CreatedAt,
                    Listing  = listing  == null ? null : new { listing.Id, listing.Title, listing.Price },
                    Customer = customer == null ? null : new { customer.Id, customer.Name, customer.Email },
                    Lister   = lister   == null ? null : new { lister.Id,   lister.Name,   lister.Email },
                    Messages = messages.Select(m => new { m.Id, m.SenderId, m.Text, m.SentAt, m.ReadAt })
                }
            });
        }

        // DELETE /api/admin/inquiries/{id}
        [HttpDelete("inquiries/{id}")]
        public async Task<IActionResult> DeleteInquiry(Guid id)
        {
            var inq = await _db.Inquiries.FirstOrDefaultAsync(i => i.Id == id);
            if (inq == null) return NotFound(new { message = "Inquiry not found." });

            _db.Messages.RemoveRange(_db.Messages.Where(m => m.InquiryId == id));
            _db.Inquiries.Remove(inq);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Inquiry deleted." });
        }

        // DELETE /api/admin/messages/{id}
        [HttpDelete("messages/{id}")]
        public async Task<IActionResult> DeleteMessage(Guid id)
        {
            var msg = await _db.Messages.FirstOrDefaultAsync(m => m.Id == id);
            if (msg == null) return NotFound(new { message = "Message not found." });

            _db.Messages.Remove(msg);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Message deleted." });
        }

        // ============================================================
        // Notifications (admin → users)
        // ============================================================

        // POST /api/admin/notifications
        [HttpPost("notifications")]
        public async Task<IActionResult> SendNotification([FromBody] AdminNotificationDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Body))
                return BadRequest(new { message = "Title and Body are required." });

            // Targeted send
            if (dto.UserId.HasValue)
            {
                var target = await _userManager.FindByIdAsync(dto.UserId.Value.ToString());
                if (target == null) return NotFound(new { message = "Target user not found." });

                await _unitOfWork.Notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = dto.UserId.Value,
                    Title = dto.Title,
                    Body = dto.Body,
                    Type = NotificationType.NewMatch,
                    Seen = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _unitOfWork.SaveAsync();
                return Ok(new { message = "Notification sent.", recipients = 1 });
            }

            // Broadcast
            IQueryable<ApplicationUser> q = _userManager.Users;
            if (!string.IsNullOrWhiteSpace(dto.Role))
            {
                if (string.Equals(dto.Role, "Customer", StringComparison.OrdinalIgnoreCase))
                    q = q.Where(u => u.UserType == UserType.Customer);
                else if (string.Equals(dto.Role, "Lister", StringComparison.OrdinalIgnoreCase))
                    q = q.Where(u => u.UserType == UserType.Lister);
                else if (string.Equals(dto.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                    q = q.Where(u => u.UserType == UserType.Admin);
            }

            var ids = await q.Select(u => u.Id).ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var uid in ids)
            {
                await _unitOfWork.Notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = uid,
                    Title = dto.Title,
                    Body = dto.Body,
                    Type = NotificationType.NewMatch,
                    Seen = false,
                    CreatedAt = now
                });
            }
            await _unitOfWork.SaveAsync();
            return Ok(new { message = "Broadcast sent.", recipients = ids.Count });
        }

        // ============================================================
        // Complaints (user reports on listings)
        // ============================================================

        private async Task<object> ProjectComplaint(Complaint c)
        {
            var listing  = await _db.Listings.FirstOrDefaultAsync(l => l.Id == c.ListingId);
            var reporter = await _userManager.FindByIdAsync(c.ReporterId.ToString());
            return new
            {
                c.Id,
                c.Reason,
                c.Details,
                c.Status,
                c.CreatedAt,
                c.ReviewedAt,
                c.ListingId,
                ListingTitle = listing?.Title,
                ListingReferenceNumber = listing?.ReferenceNumber,
                Reporter = reporter == null ? null : new { reporter.Id, reporter.Name, reporter.Email }
            };
        }

        // GET /api/admin/complaints?status=open
        [HttpGet("complaints")]
        public async Task<IActionResult> GetAllComplaints([FromQuery] string? status = null)
        {
            var q = _db.Complaints.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ComplaintStatus>(status, true, out var st))
                q = q.Where(c => c.Status == st);

            var complaints = await q.OrderByDescending(c => c.CreatedAt).ToListAsync();
            var result = new List<object>();
            foreach (var c in complaints)
                result.Add(await ProjectComplaint(c));

            return Ok(new { message = "Complaints retrieved.", data = result });
        }

        // GET /api/admin/complaints/{id}
        [HttpGet("complaints/{id}")]
        public async Task<IActionResult> GetComplaintById(Guid id)
        {
            var c = await _db.Complaints.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Complaint not found." });
            return Ok(new { message = "Complaint retrieved.", data = await ProjectComplaint(c) });
        }

        // PUT /api/admin/complaints/{id}/resolve
        [HttpPut("complaints/{id}/resolve")]
        public async Task<IActionResult> ResolveComplaint(Guid id, [FromBody] ResolveComplaintRequest dto)
        {
            var c = await _db.Complaints.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Complaint not found." });

            c.Status = dto.Status;
            c.ReviewedAt = DateTime.UtcNow;
            if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
                c.ReviewedBy = adminId;

            await _db.SaveChangesAsync();
            return Ok(new { message = "Complaint updated.", data = await ProjectComplaint(c) });
        }

        // ============================================================
        // Stats
        // ============================================================

        // GET /api/admin/stats
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var totalUsers      = await _userManager.Users.CountAsync();
            var suspendedUsers  = await _userManager.Users.CountAsync(u => u.AccountStatus == AccountStatus.Suspended);
            var bannedUsers     = await _userManager.Users.CountAsync(u => u.AccountStatus == AccountStatus.Banned);
            var verifiedListers = await _userManager.Users.CountAsync(u => u.UserType == UserType.Lister && u.IsVerified);

            var totalListings   = await _db.Listings.CountAsync();
            var pendingListings = await _db.Listings.CountAsync(l => !l.IsApproved && l.Status != ListingStatus.Archived);
            var activeListings  = await _db.Listings.CountAsync(l => l.IsApproved && l.Status == ListingStatus.Active);
            var rejectedListings= await _db.Listings.CountAsync(l => l.Status == ListingStatus.Archived);
            var totalViews      = await _db.Listings.SumAsync(l => (int?)l.ViewCount) ?? 0;

            var totalInquiries  = await _db.Inquiries.CountAsync();
            var openInquiries   = await _db.Inquiries.CountAsync(i => i.Status != InquiryStatus.Closed);

            var totalComplaints = await _db.Complaints.CountAsync();
            var openComplaints  = await _db.Complaints.CountAsync(c => c.Status == ComplaintStatus.Open);

            return Ok(new
            {
                users = new { totalUsers, suspendedUsers, bannedUsers, verifiedListers },
                listings = new { totalListings, pendingListings, activeListings, rejectedListings, totalViews },
                inquiries = new { totalInquiries, openInquiries },
                complaints = new { totalComplaints, openComplaints }
            });
        }
    }

    // ============================================================
    // DTOs
    // ============================================================
    public class AssignRoleDto
    {
        public string Role { get; set; } = "";
    }

    public class RejectListingDto
    {
        public string RejectionReason { get; set; } = "";
    }

    public class BanUserDto
    {
        public string? Reason { get; set; }
    }

    public class SuspendUserDto
    {
        public int Days { get; set; }
        public string? Reason { get; set; }
    }

    public class VerifyUserDto
    {
        public bool? IsVerified { get; set; }
    }

    public class AdminNotificationDto
    {
        public Guid? UserId { get; set; }
        public string? Role { get; set; }
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
    }
}
