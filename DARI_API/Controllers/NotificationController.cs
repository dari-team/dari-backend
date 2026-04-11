using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotificationController : Controller
{
    private readonly IUnitOfWork _unitOfWork;

    public NotificationController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    private Guid GetUserId()
    {
        return Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
    }

    // Get my notifications
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = GetUserId();

        var notifications = await _unitOfWork.Notifications
            .FindAsync(x => x.UserId == userId);

        return Ok(notifications.OrderByDescending(x => x.CreatedAt));
    }

    // Mark as seen
    [HttpPut("seen/{id}")]
    public async Task<IActionResult> MarkAsSeen(Guid id)
    {
        var notification = await _unitOfWork.Notifications.GetByIdAsync(id);

        if (notification == null)
            return NotFound();

        notification.Seen = true;

        _unitOfWork.Notifications.Update(notification);
        await _unitOfWork.SaveAsync();

        return Ok();
    }
}