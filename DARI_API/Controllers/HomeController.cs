using Microsoft.AspNetCore.Mvc;

namespace DARI_API.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
