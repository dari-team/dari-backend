using DARI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DARI.Data;
using DARI.ViewModels;
using DARI_API.Models;
using System;

namespace DARI.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppDbContext _context;

        public AdminController(UserManager<ApplicationUser> userManager, AppDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        // ─── View All Users ──────────────────────────────────
        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users.ToListAsync();
            return View(users);
        }

        // ─── Ban User ────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> BanUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            user.AccountStatus = AccountStatus.Suspended;
            user.UpdatedAt = DateTime.UtcNow;

            await _userManager.UpdateAsync(user);

            TempData["Success"] = $"{user.Name} has been banned.";
            return RedirectToAction("Users");
        }

        // ─── Assign Role ─────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AssignRole(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);

            var model = new AssignRoleViewModel
            {
                UserId = id,
                UserName = user.Name,
                CurrentRole = currentRoles.FirstOrDefault()
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AssignRole(AssignRoleViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);

            if (user == null)
                return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, model.NewRole);

            TempData["Success"] = $"Role updated to {model.NewRole} for {user.Name}.";
            return RedirectToAction("Users");
        }

        // ─── View All Pending Listings ────────────────────────
        public async Task<IActionResult> PendingListings()
        {
            var listings = await _context.Listings
                .Where(l => l.Status == ListingStatus.Pending)
                .Include(l => l.Owner)
                .ToListAsync();

            return View(listings);
        }

        // ─── Approve Listing ──────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ApproveListing(int id)
        {
            var listing = await _context.Listings.FindAsync(id);

            if (listing == null)
                return NotFound();

            listing.Status = ListingStatus.Approved;
            listing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Listing approved successfully.";
            return RedirectToAction("PendingListings");
        }

        // ─── Reject Listing ───────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> RejectListing(int id)
        {
            var listing = await _context.Listings.FindAsync(id);

            if (listing == null)
                return NotFound();

            listing.Status = ListingStatus.Rejected;
            listing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Listing rejected.";
            return RedirectToAction("PendingListings");
        }
    }
}