using DARI_API.ViewModels;
using Microsoft.AspNetCore.Mvc;
using DARI_API.Models;

namespace DARI_API.Controllers
{
    [ApiController]
    [Route("Api/[controller]")]
    public class ListingController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;

        public ListingController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var Listings = await _unitOfWork.Listings.GetAllAsync();
            return Ok(Listings);
        }

        [HttpPost]
        public async Task<IActionResult> create(ListingViewModel listing)
        {
            var data = new Listing
            {
                Id = Guid.NewGuid(),
                Title = listing.title,
                Price = listing.price,
            };
            await _unitOfWork.Listings.AddAsync(data);
            await _unitOfWork.SaveAsync();
            return Ok(data);
        }

    }
}
