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

        // CUSTOMER ONLY
        [Authorize(Roles = "Customer")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyInquiries()
        {
            var customerId = GetUserId();

            var inquiries = await _unitOfWork.Inquiries.FindAsync(x => x.CustomerId == customerId);

            var result = new List<object>();

            foreach (var inquiry in inquiries.OrderByDescending(x => x.CreatedAt))
            {
                var messages = await _unitOfWork.Messages.FindAsync(x => x.InquiryId == inquiry.Id);

                result.Add(new
                {
                    inquiry.Id,
                    inquiry.CustomerId,
                    inquiry.ListerId,
                    inquiry.ListingId,
                    inquiry.Status,
                    inquiry.CreatedAt,
                    Messages = messages.OrderBy(x => x.SentAt).Select(m => new
                    {
                        m.Id,
                        m.SenderId,
                        m.Text,
                        m.SentAt,
                        m.ReadAt
                    })
                });
            }

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
            {
                var messages = await _unitOfWork.Messages.FindAsync(x => x.InquiryId == inquiry.Id);

                result.Add(new
                {
                    inquiry.Id,
                    inquiry.CustomerId,
                    inquiry.ListerId,
                    inquiry.ListingId,
                    inquiry.Status,
                    inquiry.CreatedAt,
                    Messages = messages.OrderBy(x => x.SentAt).Select(m => new
                    {
                        m.Id,
                        m.SenderId,
                        m.Text,
                        m.SentAt,
                        m.ReadAt
                    })
                });
            }

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

            var messages = await _unitOfWork.Messages.FindAsync(x => x.InquiryId == id);

            return Ok(new
            {
                inquiry.Id,
                inquiry.CustomerId,
                inquiry.ListerId,
                inquiry.ListingId,
                inquiry.Status,
                inquiry.CreatedAt,
                Messages = messages.OrderBy(x => x.SentAt).Select(m => new
                {
                    m.Id,
                    m.SenderId,
                    m.Text,
                    m.SentAt,
                    m.ReadAt
                })
            });
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
