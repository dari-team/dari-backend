using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Authorization;
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

        public InquiryController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
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

        //CUSTOMER ONLY
        [Authorize(Roles = "Customer")]
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

        //CUSTOMER ONLY
        [Authorize(Roles = "Customer")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyInquiries()
        {
            var customerId = GetUserId();

            var inquiries = await _unitOfWork.Inquiries.FindAsync(x => x.CustomerId == customerId);

            return Ok(inquiries.OrderByDescending(x => x.CreatedAt));
        }

        //LISTER ONLY
        [Authorize(Roles = "Lister")]
        [HttpGet("received")]
        public async Task<IActionResult> GetReceivedInquiries()
        {
            var listerId = GetUserId();

            var inquiries = await _unitOfWork.Inquiries.FindAsync(x => x.ListerId == listerId);

            return Ok(inquiries.OrderByDescending(x => x.CreatedAt));
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

            return Ok(inquiry);
        }


        //LISTER ONLY
        [Authorize(Roles = "Lister")]
        [HttpPut("respond/{id}")]
        public async Task<IActionResult> Respond(Guid id)
        {
            var userId = GetUserId();

            var inquiry = await _unitOfWork.Inquiries.GetByIdAsync(id);

            if (inquiry == null)
                return NotFound();

            if (inquiry.ListerId != userId)
                return Forbid();

            //checks status first
            if (inquiry.Status == InquiryStatus.Closed)
                return BadRequest("Inquiry is already closed");

            inquiry.Status = InquiryStatus.Responded;

            _unitOfWork.Inquiries.Update(inquiry);

            //Notification without a real message
            await CreateNotification(
                inquiry.CustomerId,
                "Inquiry Responded",
                "A lister has overlooked your inquiry",
                NotificationType.InquiryResponse
            );

            await _unitOfWork.SaveAsync();

            return Ok(inquiry);
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
