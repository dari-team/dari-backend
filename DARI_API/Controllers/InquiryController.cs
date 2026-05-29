using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DARI_API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class InquiryController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;

        public InquiryController(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
        }

        // Build the enriched shape shared by all list/detail endpoints.
        private async Task<object> EnrichInquiry(Inquiry inquiry)
        {
            var listing  = await _unitOfWork.Listings.GetByIdAsync(inquiry.ListingId);
            var address  = listing != null
                ? (await _unitOfWork.Addresses.FindAsync(a => a.ListingId == listing.Id)).FirstOrDefault()
                : null;
            var customer = await _userManager.FindByIdAsync(inquiry.CustomerId.ToString());
            var lister   = await _userManager.FindByIdAsync(inquiry.ListerId.ToString());
            var messages = await _unitOfWork.Messages.FindAsync(m => m.InquiryId == inquiry.Id);

            return new
            {
                inquiry.Id,
                inquiry.CustomerId,
                CustomerName = customer?.Name ?? "",
                CustomerProfilePictureUrl = customer?.ProfilePictureUrl,
                inquiry.ListerId,
                ListerName = lister?.Name ?? "",
                ListerProfilePictureUrl = lister?.ProfilePictureUrl,
                inquiry.ListingId,
                ListingTitle    = listing?.Title ?? "",
                // Localized titles so the client can render in the viewer's language.
                ListingTitleEn  = listing?.TitleEn,
                ListingTitleAr  = listing?.TitleAr,
                ListingCity   = address?.City  ?? "",
                ListingPrice  = listing?.Price ?? 0,
                ListingType   = listing?.ListingType,
                inquiry.Status,
                inquiry.CreatedAt,
                Messages = messages.OrderBy(m => m.SentAt).Select(m => new
                {
                    m.Id,
                    m.SenderId,
                    m.Text,
                    m.SentAt,
                    m.ReadAt
                })
            };
        }

        private Guid GetUserId()
        {
            return Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        }

        private async Task CreateNotification(Guid userId, string title, string body, NotificationType type)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                Body = body,
                Type = type,
                Seen = false,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Notifications.AddAsync(notification);
        }

        //CUSTOMER + LISTER (listers/agents may inquire on listings they don't own)
        [Authorize(Roles = "Customer,Lister")]
        [HttpPost]
        public async Task<IActionResult> Create(InquiryViewModel model)
        {
            var customerId = GetUserId();

            var listing = await _unitOfWork.Listings.GetByIdAsync(model.ListingId);

            //Listing existence check
            if (listing == null)
                return NotFound("Listing not found");

            //Approved listing check
            //if (!listing.IsApproved)
            //    return BadRequest("You can only inquire about approved listings");

            //Duplicate check
            var existingInquiry = (await _unitOfWork.Inquiries.FindAsync(x =>
                x.CustomerId == customerId &&
                x.ListingId == model.ListingId &&
                x.Status != InquiryStatus.Closed)).FirstOrDefault();

            if (existingInquiry != null)
                return BadRequest("You already have an open inquiry for this listing");

            //Creates inquiry
            var inquiry = new Inquiry
            {
                Id = Guid.NewGuid(),
                ListingId = model.ListingId,
                CustomerId = customerId,
                ListerId = listing.ListerId,
                Status = InquiryStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Inquiries.AddAsync(inquiry);

            //Initial message
            var message = new Message
            {
                Id = Guid.NewGuid(),
                InquiryId = inquiry.Id,
                SenderId = customerId,
                Text = model.Message,      
                SentAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(message);

            //Notify lister of new inquiry
            await CreateNotification(
                listing.ListerId,
                "New Inquiry",
                model.Message,
                NotificationType.NewMatch
            );

            await _unitOfWork.SaveAsync();

            return Ok(inquiry);
        }

        // CUSTOMER + LISTER (so a lister can see inquiries they sent)
        [Authorize(Roles = "Customer,Lister")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyInquiries()
        {
            var customerId = GetUserId();

            var inquiries = await _unitOfWork.Inquiries.FindAsync(x => x.CustomerId == customerId);

            var result = new List<object>();
            foreach (var inquiry in inquiries.OrderByDescending(x => x.CreatedAt))
                result.Add(await EnrichInquiry(inquiry));
            return Ok(result);
        }

        //LISTER ONLY
        [Authorize(Roles = "Lister")]
        [HttpGet("received")]
        public async Task<IActionResult> GetReceivedInquiries()
        {
            var listerId = GetUserId();
            var inquiries = await _unitOfWork.Inquiries.FindAsync(x => x.ListerId == listerId);
            var result = new List<object>();
            foreach (var inquiry in inquiries.OrderByDescending(x => x.CreatedAt))
                result.Add(await EnrichInquiry(inquiry));
            return Ok(result);
        }

        //ALL
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = GetUserId();

            var inquiry = await _unitOfWork.Inquiries.GetByIdAsync(id);

            if (inquiry == null)
                return NotFound();

            if (inquiry.CustomerId != userId && inquiry.ListerId != userId)
                return Forbid();

            return Ok(await EnrichInquiry(inquiry));
        }

        [HttpPost("{id}/messages")]
        public async Task<IActionResult> SendMessage(Guid id, MessageViewModel model)
        {
            var userId = GetUserId();

            var inquiry = await _unitOfWork.Inquiries.GetByIdAsync(id);

            if (inquiry == null)
                return NotFound();

            if (inquiry.CustomerId != userId && inquiry.ListerId != userId)
                return Forbid();

            if (inquiry.Status == InquiryStatus.Closed)
                return BadRequest("Cannot send messages to a closed inquiry");

            var message = new Message
            {
                Id = Guid.NewGuid(),
                InquiryId = id,
                SenderId = userId,
                Text = model.Text,
                SentAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(message);

            var recipientId = userId == inquiry.CustomerId ? inquiry.ListerId : inquiry.CustomerId;

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = recipientId,
                Title = "New Message",
                Body = model.Text,
                Type = NotificationType.NewMessage,
                Seen = false,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Notifications.AddAsync(notification);
            await _unitOfWork.SaveAsync();

            return Ok(message);
        }


        //ALL
        [HttpPut("close/{id}")]
        public async Task<IActionResult> Close(Guid id)
        {
            var userId = GetUserId();

            var inquiry = await _unitOfWork.Inquiries.GetByIdAsync(id);

            if (inquiry == null)
                return NotFound();

            //if both true == forbid access
            if (inquiry.CustomerId != userId && inquiry.ListerId != userId)
                return Forbid();

            //checks status first
            if (inquiry.Status == InquiryStatus.Closed)
                return BadRequest("Inquiry is already closed");

            inquiry.Status = InquiryStatus.Closed;

            _unitOfWork.Inquiries.Update(inquiry);
            await _unitOfWork.SaveAsync();

            return Ok("Inquiry closed");
        }
    }
}
